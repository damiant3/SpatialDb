using SparseLattice.Gguf;
///////////////////////////////////////////////
namespace SparseLattice.Test.Gguf;

[TestClass]
public sealed class BpeTokenizerTests
{
    // -----------------------------------------------------------------------
    // Helpers — minimal vocab + merge table builders
    // -----------------------------------------------------------------------

    // Builds a minimal GPT-2-style vocab covering the byte-encoded characters
    // of "hello world" plus the expected merged tokens, and a merge table
    // that produces known token IDs.
    //
    // GPT-2 byte encoding:
    //   'h'=0x68, 'e'=0x65, 'l'=0x6C, 'o'=0x6F ? all in printable ASCII range
    //   ? map directly to themselves as single chars.
    //   Space (0x20) is NOT in the printable-sans-space range, so it maps to
    //   the GPT-2 unicode offset. In the standard GPT-2 table, byte 0x20 (32)
    //   is in the "unsafe" range (it is the space character, which is excluded
    //   from the self-mapping range '!'...'~').
    //   The safe range is b >= '!' (33) and b <= '~' (126) for the first segment,
    //   so byte 32 (space) maps to the first extended code-point: U+0100 = '?'.
    //
    // Pre-tokenisation of "hello world":
    //   The GPT-2 regex splits this into ["hello", " world"].
    //   "hello" ? bytes [h,e,l,l,o] ? chars ["h","e","l","l","o"]
    //   " world" ? bytes [space,w,o,r,l,d] ? chars ["?","w","o","r","l","d"]
    //              (space 0x20 ? U+0100 '?')
    //
    // For this minimal test we define:
    //   token  0  = "<bos>"
    //   token  1  = "<eos>"
    //   token  2  = "<unk>"
    //   token  3  = "h"
    //   token  4  = "e"
    //   token  5  = "l"
    //   token  6  = "o"
    //   token  7  = "?"   (GPT-2 encoding of space byte 0x20)
    //   token  8  = "w"
    //   token  9  = "r"
    //   token 10  = "d"
    //   token 11  = "he"        (merge rank 0: "h" + "e")
    //   token 12  = "hel"       (merge rank 1: "he" + "l")
    //   token 13  = "hell"      (merge rank 2: "hel" + "l")
    //   token 14  = "hello"     (merge rank 3: "hell" + "o")
    //   token 15  = "?w"        (merge rank 4: "?" + "w")
    //   token 16  = "?wo"       (merge rank 5: "?w" + "o")
    //   token 17  = "?wor"      (merge rank 6: "?wo" + "r")
    //   token 18  = "?worl"     (merge rank 7: "?wor" + "l")
    //   token 19  = "?world"    (merge rank 8: "?worl" + "d")

    private static BpeTokenizer BuildHelloWorldTokenizer()
    {
        // Space byte (0x20) maps to its GPT-2 unicode representative.
        // We derive this at runtime so the test does not hard-code an assumed value.
        char spaceChar = BpeTokenizer.ByteToUnicodeChar(0x20);
        string sp      = spaceChar.ToString();

        // "hello" byte-encodes as ["h","e","l","l","o"]  (all printable ASCII)
        // " world" byte-encodes as [sp,"w","o","r","l","d"]
        //
        // Merged tokens follow GPT-2 BPE merge order defined below.

        string[] tokens =
        [
            "<bos>", "<eos>", "<unk>",                         // ids 0,1,2
            "h", "e", "l", "o",                                // ids 3,4,5,6
            sp, "w", "r", "d",                                 // ids 7,8,9,10
            "he", "hel", "hell", "hello",                      // ids 11,12,13,14
            sp + "w", sp + "wo", sp + "wor",                   // ids 15,16,17
            sp + "worl", sp + "world",                         // ids 18,19
        ];

        string[] merges =
        [
            "h e",
            "he l",
            "hel l",
            "hell o",
            sp + " w",
            sp + "w o",
            sp + "wo r",
            sp + "wor l",
            sp + "worl d",
        ];

        return new BpeTokenizer(tokens, merges, bosTokenId: 0, eosTokenId: 1, unkTokenId: 2);
    }

    // -----------------------------------------------------------------------
    // Unit tests — encode
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Unit_Encode_HelloWorld_ProducesExpectedTokenIds()
    {
        BpeTokenizer tokenizer = BuildHelloWorldTokenizer();

        int[] ids = tokenizer.Encode("hello world", addSpecialTokens: false);

        // Expected: "hello" ? id 14, " world" ? id 19
        int[] expected = [14, 19];
        CollectionAssert.AreEqual(expected, ids);
    }

    [TestMethod]
    public void Unit_Encode_AddSpecialTokens_PrependsBosAppendsEos()
    {
        BpeTokenizer tokenizer = BuildHelloWorldTokenizer();

        int[] ids = tokenizer.Encode("hello world", addSpecialTokens: true);

        Assert.AreEqual(0, ids[0],              "First token must be BOS");
        Assert.AreEqual(1, ids[^1],             "Last token must be EOS");
        Assert.AreEqual(4, ids.Length,          "BOS + 2 content tokens + EOS");
    }

    [TestMethod]
    public void Unit_Encode_SpecialTokensOff_NoBosOrEos()
    {
        BpeTokenizer tokenizer = BuildHelloWorldTokenizer();

        int[] ids = tokenizer.Encode("hello world", addSpecialTokens: false);

        CollectionAssert.DoesNotContain(ids, 0, "BOS must not appear when addSpecialTokens=false");
        CollectionAssert.DoesNotContain(ids, 1, "EOS must not appear when addSpecialTokens=false");
    }

    [TestMethod]
    public void Unit_Encode_EmptyString_ReturnsOnlySpecialTokensWhenEnabled()
    {
        BpeTokenizer tokenizer = BuildHelloWorldTokenizer();

        int[] ids = tokenizer.Encode("", addSpecialTokens: true);

        Assert.AreEqual(2, ids.Length);
        Assert.AreEqual(0, ids[0]);
        Assert.AreEqual(1, ids[1]);
    }

    [TestMethod]
    public void Unit_Encode_EmptyString_ReturnsEmptyArrayWhenSpecialTokensOff()
    {
        BpeTokenizer tokenizer = BuildHelloWorldTokenizer();

        int[] ids = tokenizer.Encode("", addSpecialTokens: false);

        Assert.AreEqual(0, ids.Length);
    }

    [TestMethod]
    public void Unit_Encode_BosTokenId_IsFirstElement()
    {
        BpeTokenizer tokenizer = BuildHelloWorldTokenizer();

        int[] ids = tokenizer.Encode("hello", addSpecialTokens: true);

        Assert.AreEqual(tokenizer.BosTokenId, ids[0]);
    }

    [TestMethod]
    public void Unit_Encode_EosTokenId_IsLastElement()
    {
        BpeTokenizer tokenizer = BuildHelloWorldTokenizer();

        int[] ids = tokenizer.Encode("hello", addSpecialTokens: true);

        Assert.AreEqual(tokenizer.EosTokenId, ids[^1]);
    }

    // -----------------------------------------------------------------------
    // Unit tests — decode round-trip
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Unit_Decode_RoundTrip_HelloWorld()
    {
        BpeTokenizer tokenizer = BuildHelloWorldTokenizer();

        int[] ids    = tokenizer.Encode("hello world", addSpecialTokens: true);
        string result = tokenizer.Decode(ids);

        Assert.AreEqual("hello world", result);
    }

    [TestMethod]
    public void Unit_Decode_SkipsBosAndEos()
    {
        BpeTokenizer tokenizer = BuildHelloWorldTokenizer();

        // Manually supply BOS + content + EOS
        int[] ids     = [0, 14, 19, 1];
        string result  = tokenizer.Decode(ids);

        Assert.AreEqual("hello world", result);
    }

    [TestMethod]
    public void Unit_Decode_EmptyIds_ReturnsEmptyString()
    {
        BpeTokenizer tokenizer = BuildHelloWorldTokenizer();

        string result = tokenizer.Decode([]);

        Assert.AreEqual("", result);
    }

    // -----------------------------------------------------------------------
    // Unit tests — VocabSize
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Unit_VocabSize_MatchesTokenCount()
    {
        BpeTokenizer tokenizer = BuildHelloWorldTokenizer();
        Assert.AreEqual(20, tokenizer.VocabSize);
    }

    // -----------------------------------------------------------------------
    // Unit tests — no merges
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Unit_Encode_NoMerges_FallsBackToCharacterTokens()
    {
        // Vocab with only single-character tokens and no merges.
        // "ab" should produce individual 'a' and 'b' ids (no merging).
        string[] tokens = ["<bos>", "<eos>", "<unk>", "a", "b"];
        BpeTokenizer tokenizer = new(tokens, [], bosTokenId: 0, eosTokenId: 1, unkTokenId: 2);

        int[] ids = tokenizer.Encode("ab", addSpecialTokens: false);

        CollectionAssert.AreEqual(new int[] { 3, 4 }, ids);
    }

    // -----------------------------------------------------------------------
    // Integration tests — real GGUF file
    // -----------------------------------------------------------------------

    private static string? ResolveTestDataDir()
    {
        // Walk up from the test assembly directory to find TestData/Embeddings.
        string? dir = AppContext.BaseDirectory;
        for (int depth = 0; depth < 8 && dir is not null; depth++)
        {
            string candidate = Path.Combine(dir, "TestData", "Embeddings");
            if (Directory.Exists(candidate))
                return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    [TestMethod]
    [TestCategory("Integration")]
    public void Integration_NomicEmbedText_Tokenize_CodeSnippet()
    {
        string? testDataDir = ResolveTestDataDir();
        if (testDataDir is null)
        {
            Assert.Inconclusive("TestData/Embeddings directory not found — skipping integration test.");
            return;
        }

        string? ggufPath = OllamaModelLocator.LocateGguf("nomic-embed-text", testDataDir);
        if (ggufPath is null || !File.Exists(ggufPath))
        {
            Assert.Inconclusive("nomic-embed-text GGUF blob not found — skipping integration test.");
            return;
        }

        using GgufReader reader   = GgufReader.Open(ggufPath);
        BpeTokenizer tokenizer    = BpeTokenizer.FromGguf(reader);
        int[] tokenIds            = tokenizer.Encode("public static void Main", addSpecialTokens: true);

        Assert.IsTrue(tokenIds.Length > 0, "Encoding must produce at least one token.");
        Assert.AreEqual(tokenizer.BosTokenId, tokenIds[0], "First token must be BOS.");
        Assert.AreEqual(tokenizer.EosTokenId, tokenIds[^1], "Last token must be EOS.");
        Assert.IsTrue(tokenizer.VocabSize > 30_000,
            $"nomic-embed-text vocabulary should exceed 30 000 tokens, got {tokenizer.VocabSize}.");
    }
}
