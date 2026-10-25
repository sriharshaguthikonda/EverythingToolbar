using System;
using EverythingToolbar.Core.Search;
using Xunit;

namespace EverythingToolbar.FuzzySearch.Tests
{
    public class AdvancedSearchQueryTests
    {
        private static string Compose(AdvancedSearchFields fields)
        {
            return AdvancedSearchQuery.Compose(fields)!;
        }

        public class AllEmpty
        {
            [Fact]
            public void ReturnsNull_WhenAllFieldsNull()
            {
                Assert.Null(AdvancedSearchQuery.Compose(new AdvancedSearchFields()));
            }

            [Theory]
            [InlineData("")]
            [InlineData("   ")]
            public void ReturnsNull_WhenAllFieldsEmptyOrWhitespace(string empty)
            {
                var fields = new AdvancedSearchFields(
                    Filename: empty,
                    PathContains: empty,
                    Extension: empty,
                    Attributes: empty
                );
                Assert.Null(AdvancedSearchQuery.Compose(fields));
            }
        }

        public class SingleFields
        {
            [Fact]
            public void Filename_Alone()
            {
                Assert.Equal("report", Compose(new AdvancedSearchFields(Filename: "report")));
            }

            [Fact]
            public void PathContains_Alone()
            {
                Assert.Equal(
                    "path:\"C:\\Program Files\"",
                    Compose(new AdvancedSearchFields(PathContains: "C:\\Program Files"))
                );
            }

            [Fact]
            public void Extension_Alone()
            {
                Assert.Equal("ext:pdf", Compose(new AdvancedSearchFields(Extension: "pdf")));
            }

            [Fact]
            public void Extension_LeadingDotIsStripped()
            {
                Assert.Equal("ext:pdf", Compose(new AdvancedSearchFields(Extension: ".pdf")));
            }

            [Fact]
            public void SizeMin_Alone()
            {
                Assert.Equal("size:>=1024", Compose(new AdvancedSearchFields(SizeMinBytes: 1024)));
            }

            [Fact]
            public void SizeMax_Alone()
            {
                Assert.Equal("size:<=1048576", Compose(new AdvancedSearchFields(SizeMaxBytes: 1048576)));
            }

            [Fact]
            public void DateModifiedFrom_Alone()
            {
                Assert.Equal(
                    "dm:>=2024-01-05",
                    Compose(new AdvancedSearchFields(DateModifiedFrom: new DateTime(2024, 1, 5)))
                );
            }

            [Fact]
            public void DateModifiedTo_Alone()
            {
                Assert.Equal(
                    "dm:<=2024-12-31",
                    Compose(new AdvancedSearchFields(DateModifiedTo: new DateTime(2024, 12, 31)))
                );
            }

            [Fact]
            public void DateModifiedBoth_BecomesRange()
            {
                Assert.Equal(
                    "dm:2024-01-01..2024-06-30",
                    Compose(
                        new AdvancedSearchFields(
                            DateModifiedFrom: new DateTime(2024, 1, 1),
                            DateModifiedTo: new DateTime(2024, 6, 30)
                        )
                    )
                );
            }

            [Fact]
            public void DateCreated_Both_BecomesRange()
            {
                Assert.Equal(
                    "dc:2023-02-03..2023-11-04",
                    Compose(
                        new AdvancedSearchFields(
                            DateCreatedFrom: new DateTime(2023, 2, 3),
                            DateCreatedTo: new DateTime(2023, 11, 4)
                        )
                    )
                );
            }

            [Fact]
            public void DateCreatedFrom_Alone()
            {
                Assert.Equal(
                    "dc:>=2023-02-03",
                    Compose(new AdvancedSearchFields(DateCreatedFrom: new DateTime(2023, 2, 3)))
                );
            }

            [Fact]
            public void Attributes_Alone()
            {
                Assert.Equal("attrib:d", Compose(new AdvancedSearchFields(Attributes: "d")));
            }
        }

        public class Quoting
        {
            [Fact]
            public void Filename_WithSpace_IsQuoted()
            {
                Assert.Equal("\"annual report\"", Compose(new AdvancedSearchFields(Filename: "annual report")));
            }

            [Fact]
            public void Filename_WithoutSpace_IsNotQuoted()
            {
                Assert.Equal("report", Compose(new AdvancedSearchFields(Filename: "report")));
            }

            [Fact]
            public void Filename_EmbeddedQuote_InQuotedToken_IsDoubled()
            {
                Assert.Equal("\"a \"\"b c\"", Compose(new AdvancedSearchFields(Filename: "a \"b c")));
            }

            [Fact]
            public void Filename_EmbeddedQuote_UnquotedToken_PassedThrough()
            {
                Assert.Equal("a\"b", Compose(new AdvancedSearchFields(Filename: "a\"b")));
            }

            [Fact]
            public void PathContains_EmbeddedQuote_IsDoubled()
            {
                Assert.Equal("path:\"dir\"\"x\"", Compose(new AdvancedSearchFields(PathContains: "dir\"x")));
            }

            [Fact]
            public void Fields_AreTrimmed()
            {
                Assert.Equal("report", Compose(new AdvancedSearchFields(Filename: "  report  ")));
                Assert.Equal("ext:pdf", Compose(new AdvancedSearchFields(Extension: "pdf")));
                Assert.Equal("ext:pdf", Compose(new AdvancedSearchFields(Extension: "  pdf  ")));
            }
        }

        public class Combinations
        {
            [Fact]
            public void AllFields_SpaceJoinedInFieldOrder()
            {
                var query = Compose(
                    new AdvancedSearchFields(
                        Filename: "report 2024",
                        PathContains: "archive",
                        Extension: "pdf",
                        SizeMinBytes: 1024,
                        SizeMaxBytes: 2048,
                        DateModifiedFrom: new DateTime(2024, 1, 1),
                        DateModifiedTo: new DateTime(2024, 12, 31),
                        DateCreatedFrom: new DateTime(2023, 1, 1),
                        DateCreatedTo: new DateTime(2023, 12, 31),
                        Attributes: "a"
                    )
                );
                Assert.Equal(
                    "\"report 2024\" path:\"archive\" ext:pdf size:>=1024 size:<=2048 "
                        + "dm:2024-01-01..2024-12-31 dc:2023-01-01..2023-12-31 attrib:a",
                    query
                );
            }

            [Fact]
            public void EmptyFieldsAreSkipped_BetweenSetFields()
            {
                var query = Compose(new AdvancedSearchFields(Filename: "report", SizeMaxBytes: 4096, Attributes: "h"));
                Assert.Equal("report size:<=4096 attrib:h", query);
            }

            [Fact]
            public void NullAndEmptyStrings_BehaveIdentically()
            {
                var withNulls = Compose(new AdvancedSearchFields(Filename: "abc", PathContains: null, Extension: ""));
                var withEmpty = Compose(
                    new AdvancedSearchFields(Filename: " abc ", PathContains: "  ", Extension: null)
                );
                Assert.Equal("abc", withNulls);
                Assert.Equal(withNulls, withEmpty);
            }
        }

        public class ContentPassthrough
        {
            [Theory]
            [InlineData("pdf")]
            [InlineData("tar.gz")]
            [InlineData("*?$%")]
            public void Extension_IsShapedButNeverValidated(string extension)
            {
                Assert.Equal(
                    "ext:" + extension.TrimStart('.'),
                    Compose(new AdvancedSearchFields(Extension: extension))
                );
            }

            [Fact]
            public void Attributes_AreRawPassthrough()
            {
                Assert.Equal("attrib:xyz!", Compose(new AdvancedSearchFields(Attributes: "xyz!")));
            }

            [Fact]
            public void Size_IsEmittedAsRawBytes_NoUnitSuffix()
            {
                Assert.Equal("size:>=999999999", Compose(new AdvancedSearchFields(SizeMinBytes: 999999999)));
            }
        }
    }
}
