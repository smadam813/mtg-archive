package mtgarchive.xmage;

import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.CsvSource;

import static org.junit.jupiter.api.Assertions.assertEquals;

class CostTextTest {

    @ParameterizedTest
    @CsvSource(delimiter = '|', value = {
            "{2}{B}|0|{2}{B}",
            "{2}{B}|2|{4}{B}",
            "{B}{B}|2|{2}{B}{B}",
            "{1}{U}{B}{R}|4|{5}{U}{B}{R}",
            "{X}{R}|2|{X}{2}{R}",
            "{G/W}{G/W}|2|{2}{G/W}{G/W}",
            "''|4|{4}",
    })
    void theCommanderTaxFoldsIntoTheGenericMana(String cost, int tax, String expected) {
        assertEquals(expected, CostText.withTax(cost, tax));
    }
}
