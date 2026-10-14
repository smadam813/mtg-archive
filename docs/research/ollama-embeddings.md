# Embedding models available via Ollama

Research · 2026-08-23

**Question:** Which embedding models runnable in an Ollama container fit card-text and rules-text semantic retrieval? Cover: candidate models with dimensions and context lengths, quality signals for short technical text of the oracle-text kind, pgvector compatibility, the .NET client path to Ollama, and rough resource footprint under Docker Compose.

## TL;DR

**Recommend `qwen3-embedding:0.6b`** (1024 dims, 32K context, Apache 2.0, 639 MB): the strongest retrieval quality signal of any small Ollama-hosted model on the current MTEB English v2 benchmark, dimensions that index cleanly in pgvector HNSW, and Matryoshka support so dimensions can be cut later without re-embedding architecture changes. **Fallback: `nomic-embed-text`** (768 dims, 274 MB) as the cheapest battle-tested baseline. Avoid the qwen3 4B/8B variants at default dimensions (2560/4096) — they exceed pgvector's 2,000-dim HNSW index limit. The .NET path is **OllamaSharp** (recommended by Microsoft, implements `IEmbeddingGenerator<string, Embedding<float>>` from Microsoft.Extensions.AI) plus **Pgvector.EntityFrameworkCore** on the storage side. Footprint is modest: CPU-only inference is fine for embedding workloads; budget ~1–1.5 GB RAM for the Ollama container with the 0.6B model resident.

## What the corpus demands

Two properties of this project's corpus drive the selection:

- **Short technical English text.** Oracle text is a few lines of keyworded, symbol-heavy notation (`{2}{U}`, `{T}`); comprehensive-rules entries are numbered paragraphs. Chunked at card / rule granularity, essentially everything fits in 512 tokens. Long-context capability is therefore not a differentiator; short-text retrieval quality is.
- **Asymmetric retrieval.** Queries are "cards that do X" — short natural-language queries against short documents. Most strong models are trained with distinct query/document prefixes or instructions, and their cards say retrieval quality drops without them (Qwen3 reports a 1–5% drop when the query instruction is omitted [[Qwen3-Embedding-0.6B]](https://huggingface.co/Qwen/Qwen3-Embedding-0.6B)). Ollama's embed API passes input through verbatim — nothing in [`POST /api/embed`](https://docs.ollama.com/api/embed) applies model-specific prefixes — so **the service must prepend the documented prefixes itself**.

English-only corpus, so the multilingual strengths of `bge-m3` and `snowflake-arctic-embed2` buy nothing here.

## Candidates

All models below are first-class entries in the [Ollama embedding library](https://ollama.com/search?c=embedding) and run in the stock `ollama/ollama` container.

| Model (Ollama tag) | Params | Dims | Context | Size on disk | License | Query/doc prefixes |
|---|---|---|---|---|---|---|
| `qwen3-embedding:0.6b` | 0.6 B | 1024 (MRL 32–1024) | 32K | 639 MB | Apache 2.0 | Instruction on query side (recommended) |
| `embeddinggemma` | 300 M | 768 (MRL 512/256/128) | 2048 | ~600 MB | Gemma terms | `task: search result \| query:` / `title: … \| text:` |
| `nomic-embed-text` | 137 M (0.1 B) | 768 (MRL to 64) | 8192 native; Ollama page lists 2K | 274 MB | Apache 2.0 | `search_query:` / `search_document:` (required) |
| `mxbai-embed-large` | 335 M | 1024 (MRL) | 512 | 670 MB | Apache 2.0 | Query prefix `Represent this sentence for searching relevant passages:` |
| `snowflake-arctic-embed2` | 568 M | 1024 (MRL 256) | 8192 | 1.2 GB | Apache 2.0 | Query prefix |
| `bge-m3` | 567 M | 1024 | 8192 | 1.2 GB | MIT | None required |
| `granite-embedding:30m` | 30 M | 384 | 512 | 63 MB | Apache 2.0 | None documented |
| `all-minilm` | 22/33 M | 384 | 256 wp | ~46 MB | Apache 2.0 | None |

Sources per row: [qwen3-embedding on Ollama](https://ollama.com/library/qwen3-embedding) + [Qwen/Qwen3-Embedding-0.6B](https://huggingface.co/Qwen/Qwen3-Embedding-0.6B); [google/embeddinggemma-300m](https://huggingface.co/google/embeddinggemma-300m); [nomic-embed-text on Ollama](https://ollama.com/library/nomic-embed-text) + [nomic-ai/nomic-embed-text-v1.5](https://huggingface.co/nomic-ai/nomic-embed-text-v1.5); [mxbai-embed-large on Ollama](https://ollama.com/library/mxbai-embed-large) + [mixedbread-ai/mxbai-embed-large-v1](https://huggingface.co/mixedbread-ai/mxbai-embed-large-v1); [snowflake-arctic-embed-l-v2.0](https://huggingface.co/Snowflake/snowflake-arctic-embed-l-v2.0) + [Snowflake engineering blog](https://www.snowflake.com/en/engineering-blog/snowflake-arctic-embed-2-multilingual/); [bge-m3 on Ollama](https://ollama.com/library/bge-m3) + [BAAI/bge-m3](https://huggingface.co/BAAI/bge-m3); [granite-embedding on Ollama](https://ollama.com/library/granite-embedding) + [ibm-granite/granite-embedding-30m-english](https://huggingface.co/ibm-granite/granite-embedding-30m-english).

Also in the library but not shortlisted: `qwen3-embedding:4b` (2560 dims) and `:8b` (4096 dims) — both exceed pgvector index limits at full dimension (see below); `nomic-embed-text-v2-moe`, `paraphrase-multilingual`, `granite-embedding:278m`, `snowflake-arctic-embed` (v1), `bge-large` — multilingual- or superseded-generation models with no advantage for this corpus.

## Quality signals for short technical text

Benchmark caveat first: MTEB was revised in 2025, and **scores from the v1 leaderboard (56 datasets) are not comparable to MTEB(eng, v2) scores**. The older models report v1, the 2025 models report v2.

- **`qwen3-embedding:0.6b`** — MTEB English v2 mean **70.70**; multilingual mean 64.33. Instruction-aware: omitting the query-side instruction costs 1–5% retrieval performance. ([Qwen/Qwen3-Embedding-0.6B](https://huggingface.co/Qwen/Qwen3-Embedding-0.6B))
- **`embeddinggemma`** — MTEB English v2 task mean **69.67** at 768 dims; multilingual 61.15. ([google/embeddinggemma-300m](https://huggingface.co/google/embeddinggemma-300m))
- **`mxbai-embed-large`** — MTEB v1 average 64.68, retrieval (15 datasets) **54.39**; SOTA for BERT-large-size models as of March 2024. ([mixedbread-ai/mxbai-embed-large-v1](https://huggingface.co/mixedbread-ai/mxbai-embed-large-v1))
- **`snowflake-arctic-embed2`** (large) — BEIR nDCG@10 **55.6**; retrieval-specialized training. ([Snowflake/snowflake-arctic-embed-l-v2.0](https://huggingface.co/Snowflake/snowflake-arctic-embed-l-v2.0))
- **`nomic-embed-text`** (v1.5) — MTEB v1 62.28 at 768 dims (61.04 at 256); the card claims it surpasses OpenAI `text-embedding-ada-002` and `text-embedding-3-small` on short and long context tasks. ([nomic-ai/nomic-embed-text-v1.5](https://huggingface.co/nomic-ai/nomic-embed-text-v1.5), [Ollama page](https://ollama.com/library/nomic-embed-text))
- **`bge-m3`** — benchmarks emphasize multilingual (MIRACL/MKQA) and long-document retrieval; no standout English-short-text number. ([BAAI/bge-m3](https://huggingface.co/BAAI/bge-m3))

Reading of the signal: on the one benchmark where the two current-generation small models are directly comparable, Qwen3-0.6B leads EmbeddingGemma, and both sit a clear generation above the 2024 cohort. **No public benchmark covers MTG symbol notation** (`{T}`, `{2}{U}`, planeswalker loyalty syntax), so before freezing the schema, run a small in-domain check: ~50 hand-written "cards that do X" queries with known-good answers, scored nDCG@10 against the top two candidates. Cheap, and it also validates the prefix-handling code.

## pgvector compatibility

From the [pgvector README](https://github.com/pgvector/pgvector):

- The `vector` type holds up to 16,000 dims, but **HNSW and IVFFlat indexes accept `vector` columns only up to 2,000 dims**; `halfvec` indexes up to 4,000 dims. Beyond that: binary quantization (to 64,000), subvector indexing, or dimensionality reduction.
- Consequence: every shortlisted model (384–1024 dims) indexes directly with HNSW. `qwen3-embedding:4b` (2560) and `:8b` (4096) do **not** fit a `vector` HNSW index at full dimension — 2560 could be indexed as `halfvec`, 4096 not even then. Since qwen3 supports MRL and Ollama's embed endpoint exposes a [`dimensions` parameter](https://docs.ollama.com/api/embed), the larger variants are usable only with truncated output; simpler to stay at 0.6B/1024.
- Cosine distance (`<=>`) is supported, along with L2, inner product, L1, Hamming, and Jaccard. Postgres 13+; prebuilt `pgvector/pgvector` Docker images exist for Postgres 13–18, which slots directly into the Compose stack.

On the .NET side, [pgvector-dotnet](https://github.com/pgvector/pgvector-dotnet) ships `Pgvector` (core, Npgsql), `Pgvector.EntityFrameworkCore`, and `Pgvector.Dapper` — the `Vector`/`HalfVector` types map to `vector(n)` columns.

## .NET client path to Ollama

- **[OllamaSharp](https://github.com/awaescher/OllamaSharp)** is the path. Its README states it is "Recommended by Microsoft" and powers Semantic Kernel, .NET Aspire, and Microsoft.Extensions.AI; `OllamaApiClient` implements both `IChatClient` and **`IEmbeddingGenerator<string, Embedding<float>>`** from Microsoft.Extensions.AI, so embedding generation sits behind the standard abstraction rather than a vendor type.
- Microsoft's own **[Microsoft.Extensions.AI.Ollama](https://www.nuget.org/packages/Microsoft.Extensions.AI.Ollama) package is deprecated** with an explicit notice recommending OllamaSharp; no further updates planned. Do not take a dependency on it.
- Semantic Kernel, if it ever enters the stack, has an Ollama embeddings connector (`Microsoft.SemanticKernel.Connectors.Ollama`, prerelease, experimental `SKEXP0070`) built on OllamaSharp. ([Microsoft Learn](https://learn.microsoft.com/en-us/semantic-kernel/concepts/ai-services/embedding-generation/))
- Raw HTTP works too: [`POST /api/embed`](https://docs.ollama.com/api/embed) takes `model`, `input` (string **or array — batching is native**), optional `truncate` (default true; set `false` to error instead of silently truncating — the right choice for an ingest pipeline), `dimensions`, and `keep_alive`. Ollama also exposes an [OpenAI-compatible `/v1/embeddings`](https://docs.ollama.com/openai) endpoint (string and array inputs, custom dimensions) if a generic OpenAI-shaped client is ever preferable.

Practical notes for the ingest service: send documents in batches through the array form of `input`; apply the model's query/document prefixes in code (one place, behind the embedding interface); pin the model tag and record model+dimensions in the schema so a model swap forces a re-embed rather than silently mixing vector spaces.

## Resource footprint under Docker Compose

- Image `ollama/ollama`, port 11434, models persisted in a named volume at `/root/.ollama`. CPU-only is the documented default mode — `docker run -d -v ollama:/root/.ollama -p 11434:11434 ollama/ollama` — with GPU opt-in via `--gpus=all` (NVIDIA toolkit) if ever needed. ([Ollama Docker docs](https://docs.ollama.com/docker))
- Disk: the model blob (639 MB for qwen3-0.6b, 274 MB for nomic) in the volume.
- RAM: embedding models are an order of magnitude below the chat models Ollama's guidance is written for; the loaded model costs roughly its file size plus runtime overhead — ~1–1.5 GB container budget for qwen3-0.6b, well under 1 GB for nomic. Snowflake's numbers for a same-class model (100+ docs/sec on an A10, sub-10 ms query latency) indicate throughput is not a concern at this corpus size (~35K cards + rules) even on CPU. ([Snowflake blog](https://www.snowflake.com/en/engineering-blog/snowflake-arctic-embed-2-multilingual/))
- Models unload after 5 idle minutes by default; set `keep_alive` per request or `OLLAMA_KEEP_ALIVE` on the container to avoid reload latency on sporadic queries (a negative value pins the model in memory). `OLLAMA_MAX_LOADED_MODELS` defaults to 3 on CPU — fine for one embedding model, relevant only if a chat model later shares the container. ([Ollama FAQ](https://docs.ollama.com/faq))
- One config trap: `nomic-embed-text` natively supports 8192 tokens ([HF card](https://huggingface.co/nomic-ai/nomic-embed-text-v1.5)) but its [Ollama page](https://ollama.com/library/nomic-embed-text) lists a 2K context; inputs beyond the active context are **silently truncated** by default (`truncate: true`). Card text never gets near the limit, but long comprehensive-rules sections could — chunk at rule/subrule granularity or set `num_ctx`/`truncate: false` explicitly.

## Recommendation

1. **Primary: `qwen3-embedding:0.6b`** — best comparable quality signal (MTEB v2 English 70.70), 1024 dims → clean pgvector HNSW + cosine, Apache 2.0, 32K context removes all truncation worry, MRL + the API `dimensions` parameter keep a shrink path open, 639 MB / CPU-friendly.
2. **Baseline for comparison: `nomic-embed-text`** — 274 MB, 768 dims, the most widely deployed embedding model on Ollama (83M pulls); a good control in the in-domain eval and a fallback if the primary underwhelms.
3. `embeddinggemma` is the close runner-up (69.67), held back only by the Gemma use-terms license versus Qwen's Apache 2.0 and its 2048-token context.
4. Skip at full dims: `qwen3-embedding:4b`/`8b` (exceed pgvector's 2,000-dim HNSW limit); skip for this corpus: the multilingual specialists (`bge-m3`, `snowflake-arctic-embed2`, `granite-embedding:278m`).
5. Before freezing the schema, run the ~50-query in-domain eval described above; whichever model wins, store `(model, dimensions)` alongside the vectors so a future swap forces a re-embed.

## Sources

- https://ollama.com/search?c=embedding — embedding model catalog
- https://ollama.com/library/qwen3-embedding · https://ollama.com/library/nomic-embed-text · https://ollama.com/library/mxbai-embed-large · https://ollama.com/library/snowflake-arctic-embed2 · https://ollama.com/library/bge-m3 · https://ollama.com/library/granite-embedding — Ollama model pages (sizes, context, variants)
- https://huggingface.co/Qwen/Qwen3-Embedding-0.6B · https://huggingface.co/google/embeddinggemma-300m · https://huggingface.co/nomic-ai/nomic-embed-text-v1.5 · https://huggingface.co/mixedbread-ai/mxbai-embed-large-v1 · https://huggingface.co/Snowflake/snowflake-arctic-embed-l-v2.0 · https://huggingface.co/BAAI/bge-m3 · https://huggingface.co/ibm-granite/granite-embedding-30m-english — first-party model cards (dims, context, licenses, prefixes, benchmarks)
- https://www.snowflake.com/en/engineering-blog/snowflake-arctic-embed-2-multilingual/ — Arctic Embed 2.0 announcement (params, throughput)
- https://github.com/pgvector/pgvector — types, index dimension limits, operators, Docker images
- https://github.com/pgvector/pgvector-dotnet — .NET packages for pgvector
- https://docs.ollama.com/api/embed — embed endpoint (batching, truncate, dimensions, keep_alive)
- https://docs.ollama.com/openai — OpenAI-compatible `/v1/embeddings`
- https://docs.ollama.com/docker — container deployment
- https://docs.ollama.com/faq — keep_alive default, OLLAMA_MAX_LOADED_MODELS
- https://github.com/awaescher/OllamaSharp — .NET client, Microsoft.Extensions.AI interfaces
- https://www.nuget.org/packages/Microsoft.Extensions.AI.Ollama — deprecation notice
- https://learn.microsoft.com/en-us/semantic-kernel/concepts/ai-services/embedding-generation/ — Semantic Kernel Ollama connector
