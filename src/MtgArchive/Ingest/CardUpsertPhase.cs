using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MtgArchive.CollectionImport;
using MtgArchive.Data;
using MtgArchive.Embedding;

namespace MtgArchive.Ingest;

/// <summary>
/// Upserts cards by <c>oracle_id</c>, diffing the validated snapshot against
/// current <c>card</c> rows — the DB is the diff baseline; there is no
/// snapshot file archive. Populates faces, rulings, Oracle Tags, and the
/// card documents, computes every derived value, overwrites <c>raw</c> with
/// the latest record, and marks cards absent from the snapshot stale (never
/// deleted).
/// The changed count tracks gameplay-relevant content. The upsert refreshes
/// <c>edhrec_rank</c>, <c>raw</c>, and <c>digital_only</c> without counting
/// them, so routine runs don't report the whole corpus as changed. One
/// SaveChanges commits the phase.
/// </summary>
public sealed class CardUpsertPhase(ArchiveDbContext db, EmbeddingModel model) : IIngestPhase
{
    public string Name => IngestPhases.CardUpsert;

    public async Task RunAsync(IngestContext context, CancellationToken cancellationToken)
    {
        var snapshot = context.Snapshot
            ?? throw new InvalidOperationException("Card upsert needs the fetch phase's snapshot.");
        var existing = await db.Cards
            .Include(c => c.Faces)
            .Include(c => c.Rulings)
            .Include(c => c.OracleTags)
            .AsSplitQuery()
            .ToDictionaryAsync(c => c.OracleId, cancellationToken);
        // Documents come without their vectors: only the text is compared.
        var documents = (await db.CardFaceDocuments
                .Select(d => new { d.Id, d.CardId, d.FaceIndex, d.Text })
                .ToListAsync(cancellationToken))
            .ToLookup(d => d.CardId, d => new CardFaceDocument
            {
                Id = d.Id,
                CardId = d.CardId,
                FaceIndex = d.FaceIndex,
                Text = d.Text,
            });

        var present = new HashSet<Guid>();
        foreach (var record in snapshot.Cards)
        {
            present.Add(record.OracleId);
            var desired = Map(record, snapshot);
            if (!existing.TryGetValue(record.OracleId, out var card))
            {
                db.Cards.Add(desired);
                context.Report.NewCards++;
            }
            else if (Apply(card, desired))
            {
                context.Report.ChangedCards++;
            }
        }

        foreach (var card in existing.Values.Where(card => !present.Contains(card.OracleId)))
        {
            card.Stale = true;
        }

        var unknownSymbols = new SymbolCounter();
        foreach (var card in db.Cards.Local.ToList())
        {
            if (card.Stale)
            {
                db.CardFaceDocuments.RemoveRange(documents[card.Id]);
            }
            else
            {
                ComposeDocuments(card, documents[card.Id], unknownSymbols);
            }
        }

        NoteOrphans(context, "ruling", snapshot.RulingsByOracleId.Keys, present);
        NoteOrphans(context, "Oracle Tag", snapshot.TagsByOracleId.Keys, present);
        NoteUnknownSymbols(context, unknownSymbols);
        await ReplaceTagAliasesAsync(snapshot.TagAliases, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        // The report lists the corpus's stale set after this run — cards
        // marked now and cards still missing from earlier runs alike.
        context.Report.StaleCards.Clear();
        context.Report.StaleCards.AddRange(await db.Cards
            .Where(c => c.Stale)
            .OrderBy(c => c.Name)
            .Select(c => new StaleCardReport(c.OracleId, c.Name))
            .ToListAsync(cancellationToken));

        // Collection rows pointing at a stale card stay valid (resolution
        // happened at import time); queries exclude the card downstream, and
        // this is the notice.
        context.Report.StaleCollectionRows = await CollectionCardRow
            .Query(db, db.Cards.Where(c => c.Stale))
            .ToListAsync(cancellationToken);
    }

    /// <summary>The alias table is the file's alias map and nothing else.
    /// The diff removes rows the file dropped, updates relabeled ones, and
    /// adds new ones. It touches no card row, so an alias change alone
    /// never counts a card as changed or queues a re-embed.</summary>
    private async Task ReplaceTagAliasesAsync(IReadOnlyDictionary<string, string> desired, CancellationToken cancellationToken)
    {
        var current = await db.TagAliases.ToDictionaryAsync(a => a.Alias, cancellationToken);
        db.TagAliases.RemoveRange(current.Values.Where(a => !desired.ContainsKey(a.Alias)));
        foreach (var (alias, label) in desired)
        {
            if (current.TryGetValue(alias, out var row))
            {
                row.Label = label;
            }
            else
            {
                db.TagAliases.Add(new TagAlias { Alias = alias, Label = label });
            }
        }
    }

    /// <summary>
    /// The card's documents land in the same commit as the card, so a run
    /// that fails in a later phase can never leave a document behind its
    /// card. Unchanged text keeps its vector; new or changed text waits,
    /// vector-less, for the re-embed phase.
    /// </summary>
    private void ComposeDocuments(Card card, IEnumerable<CardFaceDocument> current, SymbolCounter unknownSymbols)
    {
        var byFace = current.ToDictionary(d => d.FaceIndex);
        var texts = CardDocument.Compose(card, model.Composition, unknownSymbols);
        foreach (var (text, faceIndex) in texts.Select((text, index) => (text, index)))
        {
            if (!byFace.Remove(faceIndex, out var document))
            {
                card.Documents.Add(new CardFaceDocument { FaceIndex = faceIndex, Text = text });
            }
            else if (document.Text != text)
            {
                // Update writes every column, the null vector included.
                document.Text = text;
                db.CardFaceDocuments.Update(document);
            }
        }

        db.CardFaceDocuments.RemoveRange(byFace.Values);
    }

    private static void NoteOrphans(IngestContext context, string what, IEnumerable<Guid> oracleIds, HashSet<Guid> present)
    {
        var orphans = oracleIds.Count(id => !present.Contains(id));
        if (orphans > 0)
        {
            context.Report.Errors.Add(
                $"{orphans} {what} group(s) reference oracle_ids absent from the card snapshot; ignored");
        }
    }

    /// <summary>Symbols the verbalizer passed through verbatim, counted per
    /// symbol so a new Scryfall symbol appears in the run report. The report
    /// has one notice channel, <c>errors</c>; the run still succeeds.</summary>
    private static void NoteUnknownSymbols(IngestContext context, SymbolCounter unknownSymbols)
    {
        if (unknownSymbols.Total > 0)
        {
            var counts = unknownSymbols.Ordered.Select(pair => $"{pair.Key} x{pair.Value}");
            context.Report.Errors.Add(
                $"{unknownSymbols.Total} symbol(s) in oracle text unknown to the verbalizer, embedded verbatim: "
                + string.Join(", ", counts));
        }
    }

    internal static Card Map(SnapshotCard record, OracleSnapshot snapshot)
    {
        var derived = CardDerivations.Derive(record);
        return new Card
        {
            OracleId = record.OracleId,
            Name = record.Name,
            Layout = record.Layout,
            ManaCost = record.ManaCost,
            ManaValue = derived.ManaValue,
            TypeLine = record.TypeLine,
            Supertypes = derived.Supertypes,
            CardTypes = derived.CardTypes,
            Subtypes = derived.Subtypes,
            OracleText = record.OracleText,
            Colors = record.Colors,
            ColorIdentity = record.ColorIdentity,
            Keywords = record.Keywords,
            Power = record.Power,
            Toughness = record.Toughness,
            Loyalty = record.Loyalty,
            ProducedMana = record.ProducedMana,
            GameChanger = record.GameChanger,
            EdhrecRank = record.EdhrecRank,
            ReleasedAt = record.ReleasedAt,
            Raw = JsonDocument.Parse(record.RawJson),
            CanBeCommander = derived.CanBeCommander,
            CanBeBrawlCommander = derived.CanBeBrawlCommander,
            IsMeldResult = record.IsMeldResult,
            DigitalOnly = derived.DigitalOnly,
            HasPartner = derived.HasPartner,
            HasPartnerWith = derived.HasPartnerWith,
            HasFriendsForever = derived.HasFriendsForever,
            HasBackground = derived.HasBackground,
            HasDoctorsCompanion = derived.HasDoctorsCompanion,
            CopyLimit = derived.CopyLimit,
            Stale = false,
            Faces = [.. record.Faces.Select((face, index) => new CardFace
            {
                FaceIndex = index,
                Name = face.Name,
                ManaCost = face.ManaCost,
                ManaValue = face.ManaCost is { } cost ? CardDerivations.ManaValueOf(cost) : null,
                TypeLine = face.TypeLine,
                OracleText = face.OracleText,
                Colors = face.Colors,
                Power = face.Power,
                Toughness = face.Toughness,
                Loyalty = face.Loyalty,
            })],
            Rulings = [.. (snapshot.RulingsByOracleId.GetValueOrDefault(record.OracleId) ?? [])
                .OrderBy(r => r.PublishedAt).ThenBy(r => r.Comment, StringComparer.Ordinal)
                .Select(r => new CardRuling { Source = r.Source, PublishedAt = r.PublishedAt, Comment = r.Comment })],
            OracleTags = [.. (snapshot.TagsByOracleId.GetValueOrDefault(record.OracleId) ?? [])
                .OrderBy(tag => tag.Label, StringComparer.Ordinal)
                .Select(tag => new CardOracleTag { Tag = tag.Label, Inherited = tag.Inherited, Weight = tag.Weight })],
        };
    }

    /// <summary>
    /// Brings <paramref name="card"/> to the snapshot's state and reports
    /// whether gameplay-relevant content changed. <c>edhrec_rank</c> and
    /// <c>raw</c> are always refreshed but never counted — they fluctuate
    /// with every Scryfall export. The upsert also refreshes <c>layout</c>
    /// and <c>digital_only</c> without counting them, so their first fill
    /// does not report the whole corpus as changed.
    /// </summary>
    private static bool Apply(Card card, Card desired)
    {
        var changed =
            card.Name != desired.Name
            || card.ManaCost != desired.ManaCost
            || card.ManaValue != desired.ManaValue
            || card.TypeLine != desired.TypeLine
            || !card.Supertypes.SequenceEqual(desired.Supertypes)
            || !card.CardTypes.SequenceEqual(desired.CardTypes)
            || !card.Subtypes.SequenceEqual(desired.Subtypes)
            || card.OracleText != desired.OracleText
            || !SameArray(card.Colors, desired.Colors)
            || !card.ColorIdentity.SequenceEqual(desired.ColorIdentity)
            || !card.Keywords.SequenceEqual(desired.Keywords)
            || card.Power != desired.Power
            || card.Toughness != desired.Toughness
            || card.Loyalty != desired.Loyalty
            || !SameArray(card.ProducedMana, desired.ProducedMana)
            || card.GameChanger != desired.GameChanger
            || card.ReleasedAt != desired.ReleasedAt
            || card.CanBeCommander != desired.CanBeCommander
            || card.CanBeBrawlCommander != desired.CanBeBrawlCommander
            || card.IsMeldResult != desired.IsMeldResult
            || card.HasPartner != desired.HasPartner
            || card.HasPartnerWith != desired.HasPartnerWith
            || card.HasFriendsForever != desired.HasFriendsForever
            || card.HasBackground != desired.HasBackground
            || card.HasDoctorsCompanion != desired.HasDoctorsCompanion
            || card.CopyLimit != desired.CopyLimit
            || card.Stale;

        if (changed)
        {
            card.Name = desired.Name;
            card.ManaCost = desired.ManaCost;
            card.ManaValue = desired.ManaValue;
            card.TypeLine = desired.TypeLine;
            card.Supertypes = desired.Supertypes;
            card.CardTypes = desired.CardTypes;
            card.Subtypes = desired.Subtypes;
            card.OracleText = desired.OracleText;
            card.Colors = desired.Colors;
            card.ColorIdentity = desired.ColorIdentity;
            card.Keywords = desired.Keywords;
            card.Power = desired.Power;
            card.Toughness = desired.Toughness;
            card.Loyalty = desired.Loyalty;
            card.ProducedMana = desired.ProducedMana;
            card.GameChanger = desired.GameChanger;
            card.ReleasedAt = desired.ReleasedAt;
            card.CanBeCommander = desired.CanBeCommander;
            card.CanBeBrawlCommander = desired.CanBeBrawlCommander;
            card.IsMeldResult = desired.IsMeldResult;
            card.HasPartner = desired.HasPartner;
            card.HasPartnerWith = desired.HasPartnerWith;
            card.HasFriendsForever = desired.HasFriendsForever;
            card.HasBackground = desired.HasBackground;
            card.HasDoctorsCompanion = desired.HasDoctorsCompanion;
            card.CopyLimit = desired.CopyLimit;
            card.Stale = false;
        }

        if (FacesDiffer(card.Faces, desired.Faces))
        {
            card.Faces.Clear();
            card.Faces.AddRange(desired.Faces);
            changed = true;
        }

        if (RulingsDiffer(card.Rulings, desired.Rulings))
        {
            card.Rulings.Clear();
            card.Rulings.AddRange(desired.Rulings);
            changed = true;
        }

        if (TagsDiffer(card.OracleTags, desired.OracleTags))
        {
            card.OracleTags.Clear();
            card.OracleTags.AddRange(desired.OracleTags);
            changed = true;
        }

        card.EdhrecRank = desired.EdhrecRank;
        card.Raw = desired.Raw;
        card.Layout = desired.Layout;
        card.DigitalOnly = desired.DigitalOnly;
        return changed;
    }

    private static bool SameArray(string[]? left, string[]? right)
        => left is null ? right is null : right is not null && left.SequenceEqual(right);

    private static bool FacesDiffer(List<CardFace> current, List<CardFace> desired)
    {
        if (current.Count != desired.Count)
        {
            return true;
        }

        var ordered = current.OrderBy(f => f.FaceIndex).ToList();
        return ordered.Zip(desired).Any(pair =>
            pair.First.Name != pair.Second.Name
            || pair.First.ManaCost != pair.Second.ManaCost
            || pair.First.ManaValue != pair.Second.ManaValue
            || pair.First.TypeLine != pair.Second.TypeLine
            || pair.First.OracleText != pair.Second.OracleText
            || !SameArray(pair.First.Colors, pair.Second.Colors)
            || pair.First.Power != pair.Second.Power
            || pair.First.Toughness != pair.Second.Toughness
            || pair.First.Loyalty != pair.Second.Loyalty);
    }

    private static bool RulingsDiffer(List<CardRuling> current, List<CardRuling> desired)
        => !current
            .OrderBy(r => r.PublishedAt).ThenBy(r => r.Comment, StringComparer.Ordinal)
            .Select(r => (r.Source, r.PublishedAt, r.Comment))
            .SequenceEqual(desired.Select(r => (r.Source, r.PublishedAt, r.Comment)));

    private static bool TagsDiffer(List<CardOracleTag> current, List<CardOracleTag> desired)
        => !current.OrderBy(t => t.Tag, StringComparer.Ordinal).Select(t => (t.Tag, t.Inherited, t.Weight))
            .SequenceEqual(desired.Select(t => (t.Tag, t.Inherited, t.Weight)));
}
