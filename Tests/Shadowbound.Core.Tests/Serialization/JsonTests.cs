using System.Globalization;
using System.Threading;
using Shadowbound.Core.Serialization;
using Xunit;

namespace Shadowbound.Core.Tests.Serialization
{
    public class JsonTests
    {
        [Fact]
        public void RoundTrip_PreservesPrimitives()
        {
            var node = JsonValue.NewObject();
            node.Set("name", "Wanderer");
            node.Set("level", 7);
            node.Set("health", 123.5f);
            node.Set("alive", true);
            node.Set("title", JsonValue.Null());

            JsonValue parsed = JsonValue.Parse(node.ToJson());

            Assert.Equal("Wanderer", parsed.Get("name").AsString());
            Assert.Equal(7, parsed.Get("level").AsInt());
            Assert.Equal(123.5f, parsed.Get("health").AsFloat(), 3);
            Assert.True(parsed.Get("alive").AsBool());
            Assert.Equal(JsonKind.Null, parsed.Get("title").Kind);
        }

        [Fact]
        public void RoundTrip_PreservesNestedStructures()
        {
            var root = JsonValue.NewObject();
            var list = JsonValue.NewArray();

            for (int i = 0; i < 3; i++)
            {
                var item = JsonValue.NewObject();
                item.Set("id", "item" + i);
                item.Set("qty", i + 1);
                list.Add(item);
            }

            root.Set("inventory", list);

            JsonValue parsed = JsonValue.Parse(root.ToJson());
            JsonValue inventory = parsed.Get("inventory");

            Assert.Equal(3, inventory.Count);
            Assert.Equal("item1", inventory.At(1).Get("id").AsString());
            Assert.Equal(2, inventory.At(1).Get("qty").AsInt());
        }

        [Theory]
        [InlineData("plain")]
        [InlineData("with \"quotes\"")]
        [InlineData("back\\slash")]
        [InlineData("line\nbreak")]
        [InlineData("tab\there")]
        [InlineData("carriage\rreturn")]
        public void RoundTrip_EscapesAwkwardStrings(string value)
        {
            var node = JsonValue.NewObject();
            node.Set("value", value);

            string json = node.ToJson();
            JsonValue parsed = JsonValue.Parse(json);

            Assert.Equal(value, parsed.Get("value").AsString());
        }

        [Fact]
        public void RoundTrip_EscapesControlCharacters()
        {
            // A raw control character would produce JSON that no other parser
            // would accept, so it must be escaped as \u00xx.
            var node = JsonValue.NewObject();
            node.Set("value", "beep\u0007bell");

            string json = node.ToJson();

            Assert.Contains("\\u0007", json);
            Assert.Equal("beep\u0007bell", JsonValue.Parse(json).Get("value").AsString());
        }

        [Fact]
        public void RoundTrip_PreservesUnicode()
        {
            var node = JsonValue.NewObject();
            node.Set("value", "Hollow Sentinel \u2014 \u00e9t\u00e9 \u65e5\u672c");

            JsonValue parsed = JsonValue.Parse(node.ToJson());

            Assert.Equal("Hollow Sentinel \u2014 \u00e9t\u00e9 \u65e5\u672c", parsed.Get("value").AsString());
        }

        [Fact]
        public void Numbers_AreWrittenWithAnInvariantDecimalSeparator()
        {
            // On a machine whose culture uses a comma separator, a naive
            // ToString would write "1,5" and produce an unreadable save.
            CultureInfo previous = Thread.CurrentThread.CurrentCulture;

            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");

                var node = JsonValue.NewObject();
                node.Set("value", 1.5f);
                node.Set("precise", 12.345);

                string json = node.ToJson();

                Assert.DoesNotContain(",", json.Replace(",\"", "|"));
                Assert.Equal(1.5f, JsonValue.Parse(json).Get("value").AsFloat(), 5);
                Assert.Equal(12.345, JsonValue.Parse(json).Get("precise").AsNumber(), 5);
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = previous;
            }
        }

        [Fact]
        public void Numbers_AreParsedWithAnInvariantSeparatorRegardlessOfCulture()
        {
            CultureInfo previous = Thread.CurrentThread.CurrentCulture;

            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("fr-FR");

                JsonValue parsed = JsonValue.Parse("{\"value\": 2.75}");

                Assert.Equal(2.75, parsed.Get("value").AsNumber(), 5);
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = previous;
            }
        }

        [Fact]
        public void WholeNumbers_AreWrittenWithoutADecimalPoint()
        {
            var node = JsonValue.NewObject();
            node.Set("count", 42);

            // Keeps hand-edited saves readable and output byte-stable.
            Assert.Contains("42", node.ToJson());
            Assert.DoesNotContain("42.0", node.ToJson());
        }

        [Fact]
        public void EmptyContainers_AreWrittenCompactly()
        {
            var node = JsonValue.NewObject();
            node.Set("items", JsonValue.NewArray());
            node.Set("meta", JsonValue.NewObject());

            string json = node.ToJson();

            Assert.Contains("\"items\":[]", json);
            Assert.Contains("\"meta\":{}", json);
        }

        [Fact]
        public void MissingProperties_ReturnNullAndTypedFallbacks()
        {
            JsonValue parsed = JsonValue.Parse("{}");

            Assert.Null(parsed.Get("nope"));
            Assert.False(parsed.Has("nope"));

            JsonValue missing = parsed.Get("nope");
            Assert.Equal(7, missing?.AsInt(7) ?? 7);
        }

        [Fact]
        public void TypeMismatches_FallBackRatherThanThrow()
        {
            JsonValue parsed = JsonValue.Parse("{\"value\": \"text\"}");

            Assert.Equal(0, parsed.Get("value").AsInt());
            Assert.Equal(9, parsed.Get("value").AsInt(9));
            Assert.False(parsed.Get("value").AsBool());
            Assert.Equal("text", parsed.Get("value").AsString());
        }

        [Fact]
        public void Booleans_AcceptZeroAndOne()
        {
            // Keeps hand-edited saves usable.
            JsonValue parsed = JsonValue.Parse("{\"a\": 1, \"b\": 0}");

            Assert.True(parsed.Get("a").AsBool());
            Assert.False(parsed.Get("b").AsBool());
        }

        [Theory]
        [InlineData("{\"a\": 1} trailing")]
        [InlineData("{\"a\": }")]
        [InlineData("{a: 1}")]
        [InlineData("{\"a\": 1")]
        [InlineData("[1, 2")]
        [InlineData("\"unterminated")]
        [InlineData("{\"a\": 1.2.3}")]
        [InlineData("{\"a\": tru}")]
        [InlineData("")]
        public void InvalidDocuments_AreRejected(string invalid)
        {
            Assert.Throws<JsonParseException>(() => JsonValue.Parse(invalid));
        }

        [Fact]
        public void NullInput_IsRejected()
        {
            Assert.Throws<JsonParseException>(() => JsonValue.Parse(null));
        }

        [Fact]
        public void TryParse_ReportsFailureWithoutThrowing()
        {
            bool ok = JsonValue.TryParse("{broken", out JsonValue value, out string error);

            Assert.False(ok);
            Assert.Null(value);
            Assert.NotNull(error);
        }

        [Fact]
        public void ParseError_ReportsACharacterPosition()
        {
            var exception = Assert.Throws<JsonParseException>(() => JsonValue.Parse("{\"a\": @}"));

            Assert.True(exception.Position > 0);
            Assert.Contains("character", exception.Message);
        }

        [Fact]
        public void DeeplyNestedDocuments_AreRejectedRatherThanOverflowingTheStack()
        {
            // A hostile or corrupt file must not take the process down.
            string deep = new string('[', 200) + new string(']', 200);

            Assert.Throws<JsonParseException>(() => JsonValue.Parse(deep));
        }

        [Fact]
        public void ByteOrderMark_IsTolerated()
        {
            JsonValue parsed = JsonValue.Parse("\uFEFF{\"a\": 1}");

            Assert.Equal(1, parsed.Get("a").AsInt());
        }

        [Fact]
        public void WhitespaceAndFormatting_AreIgnored()
        {
            JsonValue parsed = JsonValue.Parse("  {\n  \"a\"  :  1 ,\n \"b\" : [ 1 , 2 ]\n }  ");

            Assert.Equal(1, parsed.Get("a").AsInt());
            Assert.Equal(2, parsed.Get("b").Count);
        }

        [Fact]
        public void DuplicateKeys_LastOneWins()
        {
            JsonValue parsed = JsonValue.Parse("{\"a\": 1, \"a\": 2}");

            Assert.Equal(2, parsed.Get("a").AsInt());
            Assert.Equal(1, parsed.Count);
        }

        [Fact]
        public void SettingAnExistingKey_ReplacesItWithoutDuplicating()
        {
            var node = JsonValue.NewObject();
            node.Set("a", 1);
            node.Set("a", 2);

            Assert.Equal(1, node.Count);
            Assert.Equal(2, node.Get("a").AsInt());
        }

        [Fact]
        public void Remove_DropsTheProperty()
        {
            var node = JsonValue.NewObject();
            node.Set("a", 1);
            node.Set("b", 2);

            node.Remove("a");

            Assert.False(node.Has("a"));
            Assert.True(node.Has("b"));
            Assert.Equal(1, node.Count);
        }

        [Fact]
        public void AsStringList_SkipsNonStrings()
        {
            JsonValue parsed = JsonValue.Parse("{\"a\": [\"x\", 5, \"y\", null]}");

            var list = parsed.Get("a").AsStringList();

            Assert.Equal(new[] { "x", "y" }, list);
        }

        [Fact]
        public void AsIntArray_DefaultsPerElement()
        {
            JsonValue parsed = JsonValue.Parse("{\"a\": [1, \"two\", 3]}");

            int[] values = parsed.Get("a").AsIntArray();

            Assert.Equal(new[] { 1, 0, 3 }, values);
        }

        [Fact]
        public void AsInt_ClampsRatherThanOverflowing()
        {
            JsonValue parsed = JsonValue.Parse("{\"big\": 1e30, \"negative\": -1e30}");

            Assert.Equal(int.MaxValue, parsed.Get("big").AsInt());
            Assert.Equal(int.MinValue, parsed.Get("negative").AsInt());
        }

        [Fact]
        public void IndentedOutput_IsStillValidJson()
        {
            var node = JsonValue.NewObject();
            node.Set("a", JsonValue.NewArray());
            node.Get("a").Add(1);
            node.Get("a").Add(2);

            JsonValue parsed = JsonValue.Parse(node.ToJson(true));

            Assert.Equal(2, parsed.Get("a").Count);
        }

        [Fact]
        public void Arrays_PreserveOrder()
        {
            var array = JsonValue.NewArray();
            array.Add(3);
            array.Add(1);
            array.Add(2);

            JsonValue parsed = JsonValue.Parse(array.ToJson());

            Assert.Equal(3, parsed.At(0).AsInt());
            Assert.Equal(1, parsed.At(1).AsInt());
            Assert.Equal(2, parsed.At(2).AsInt());
        }

        [Fact]
        public void OutOfRangeArrayAccess_ReturnsNull()
        {
            JsonValue array = JsonValue.NewArray();
            array.Add(1);

            Assert.Null(array.At(5));
            Assert.Null(array.At(-1));
        }
    }
}
