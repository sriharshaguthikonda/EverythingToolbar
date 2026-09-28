using System.Collections.Generic;
using EverythingToolbar.FuzzySearch.Tests.Support;
using Xunit;

namespace EverythingToolbar.FuzzySearch.Tests
{
    public class FallbackQueryPlannerTests
    {
        [Theory]
        [InlineData("neurosicence", "neuroscience")]
        [InlineData("docuemnt", "document")]
        [InlineData("attachement", "attachment")]
        [InlineData("repomsp", "repomaps")]
        [InlineData("powertyo", "powertoy")]
        [InlineData("guthikodna", "Guthikonda")]
        [InlineData("ollma", "ollama")]
        [InlineData("kanataa", "kanata")]
        public void Plan_TypoQuery_KeepsRawAndAddsCorrectionInOrGroup(string typo, string correction)
        {
            var planner = new FallbackQueryPlanner(
                candidates: FakeCandidateProvider.FromCorrections((typo, correction))
            );

            var plan = planner.Plan(typo);

            Assert.NotNull(plan);
            Assert.Equal($"<{typo}|{correction}>", plan!.CorrectedQuery);
            Assert.Contains(plan.Corrections, c => c.Original == typo && c.Corrected == correction);
        }

        [Fact]
        public void Plan_MixedQuery_CorrectsOnlyPlainLiteral()
        {
            var planner = new FallbackQueryPlanner(
                candidates: FakeCandidateProvider.FromCorrections(("attachement", "attachment"))
            );

            var plan = planner.Plan("clinical attachement ext:pdf");

            Assert.NotNull(plan);
            Assert.Equal("clinical <attachement|attachment> ext:pdf", plan!.CorrectedQuery);
            var correction = Assert.Single(plan.Corrections);
            Assert.Equal("attachement", correction.Original);
        }

        [Fact]
        public void Plan_NoCandidates_ReturnsNull()
        {
            var planner = new FallbackQueryPlanner(candidates: FakeCandidateProvider.FromCorrections());

            Assert.Null(planner.Plan("neurosicence"));
        }

        [Fact]
        public void Plan_ExactMatch_ProtectsRawTerm()
        {
            // The provider contract never returns the term itself; the planner also defends against
            // it. Zero-result gating (raw query wins when it matches) happens above the planner.
            var provider = new FakeCandidateProvider(
                new Dictionary<string, IReadOnlyList<TypoCandidate>>
                {
                    ["from"] = new[] { new TypoCandidate("from", 0, 10) },
                }
            );
            var planner = new FallbackQueryPlanner(candidates: provider);

            Assert.Null(planner.Plan("from"));
        }

        [Fact]
        public void Plan_AliasAndTypoCandidates_PutsAliasFirst()
        {
            var provider = new FakeCandidateProvider(
                new Dictionary<string, IReadOnlyList<TypoCandidate>>
                {
                    ["colour"] = new[] { new TypoCandidate("chlor", 2, 9), new TypoCandidate("color", 1, 5) },
                }
            );
            var planner = new FallbackQueryPlanner(
                new AliasStore(new[] { new AliasGroup("color", new[] { "colour" }) }),
                provider
            );

            var plan = planner.Plan("colour notes");

            Assert.NotNull(plan);
            Assert.Equal("<colour|color|chlor> notes", plan!.CorrectedQuery);
        }

        [Fact]
        public void Plan_CapsAlternativesPerTerm()
        {
            var provider = new FakeCandidateProvider(
                new Dictionary<string, IReadOnlyList<TypoCandidate>>
                {
                    ["attachement"] = new[]
                    {
                        new TypoCandidate("attachment", 1, 10),
                        new TypoCandidate("attachments", 2, 4),
                        new TypoCandidate("attach", 2, 3),
                        new TypoCandidate("attachable", 2, 2),
                        new TypoCandidate("attache", 2, 1),
                    },
                }
            );
            var planner = new FallbackQueryPlanner(candidates: provider);

            var plan = planner.Plan("attachement");

            Assert.NotNull(plan);
            Assert.Equal("<attachement|attachment|attachments|attach>", plan!.CorrectedQuery);
            Assert.Equal(FallbackQueryPlanner.MaxAlternativesPerTerm, plan.Corrections[0].Corrected.Split('|').Length);
        }

        [Fact]
        public void Plan_PreservesDistanceOrderingFromProvider()
        {
            var provider = new FakeCandidateProvider(
                new Dictionary<string, IReadOnlyList<TypoCandidate>>
                {
                    ["docuemnt"] = new[] { new TypoCandidate("document", 1, 10), new TypoCandidate("documents", 2, 4) },
                }
            );
            var planner = new FallbackQueryPlanner(candidates: provider);

            var plan = planner.Plan("docuemnt");

            Assert.NotNull(plan);
            Assert.Equal("<docuemnt|document|documents>", plan!.CorrectedQuery);
        }

        [Fact]
        public void Plan_OrdersByDistanceBeforeFrequency()
        {
            var provider = new FakeCandidateProvider(
                new Dictionary<string, IReadOnlyList<TypoCandidate>>
                {
                    ["attachement"] = new[]
                    {
                        new TypoCandidate("attachable", 2, 100),
                        new TypoCandidate("attachment", 1, 1),
                    },
                }
            );
            var planner = new FallbackQueryPlanner(candidates: provider);

            var plan = planner.Plan("attachement");

            Assert.NotNull(plan);
            Assert.Equal("<attachement|attachment|attachable>", plan!.CorrectedQuery);
        }

        [Fact]
        public void Plan_TiesBreakByFrequencyThenName()
        {
            var provider = new FakeCandidateProvider(
                new Dictionary<string, IReadOnlyList<TypoCandidate>>
                {
                    ["attachement"] = new[]
                    {
                        new TypoCandidate("attache", 2, 5),
                        new TypoCandidate("attachments", 2, 9),
                    },
                }
            );
            var planner = new FallbackQueryPlanner(candidates: provider);

            var plan = planner.Plan("attachement");

            Assert.NotNull(plan);
            Assert.Equal("<attachement|attachments|attache>", plan!.CorrectedQuery);
        }

        [Fact]
        public void Plan_WholeQueryAlias_ReplacesQueryWithCanonical()
        {
            var planner = new FallbackQueryPlanner(
                new AliasStore(new[] { new AliasGroup("repomaps", new[] { "repo map" }) })
            );

            var plan = planner.Plan("repo map");

            Assert.NotNull(plan);
            Assert.Equal("repomaps", plan!.CorrectedQuery);
            var correction = Assert.Single(plan.Corrections);
            Assert.Equal("repo map", correction.Original);
        }

        [Fact]
        public void Plan_TermAlias_KeepsRawInOrGroup()
        {
            var planner = new FallbackQueryPlanner(
                new AliasStore(new[] { new AliasGroup("color", new[] { "colour" }) })
            );

            var plan = planner.Plan("colour ext:pdf");

            Assert.NotNull(plan);
            Assert.Equal("<colour|color> ext:pdf", plan!.CorrectedQuery);
        }

        [Fact]
        public void Plan_CanonicalTypedExactly_DoesNotExpand()
        {
            var planner = new FallbackQueryPlanner(
                new AliasStore(new[] { new AliasGroup("color", new[] { "colour" }) })
            );

            Assert.Null(planner.Plan("color"));
        }

        [Fact]
        public void Plan_MultiWordCanonical_IsSkippedInTermPosition()
        {
            var planner = new FallbackQueryPlanner(
                new AliasStore(new[] { new AliasGroup("repomaps", new[] { "repo map" }) })
            );

            // "repo map" as a whole query is an alias, but here it is embedded next to structure.
            Assert.Null(planner.Plan("repo map ext:pdf"));
        }

        [Fact]
        public void Plan_WithoutAliasesOrCandidates_ReturnsNull()
        {
            var planner = new FallbackQueryPlanner();

            Assert.Null(planner.Plan("neurosicence"));
        }

        [Fact]
        public void Plan_EmptyQuery_ReturnsNull()
        {
            Assert.Null(new FallbackQueryPlanner().Plan(""));
        }
    }
}
