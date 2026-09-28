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
        public void Plan_TypoQuery_CorrectsSingleLiteral(string typo, string correction)
        {
            var planner = new FallbackQueryPlanner(
                candidates: FakeCandidateProvider.FromCorrections((typo, correction))
            );

            var plan = planner.Plan(typo);

            Assert.NotNull(plan);
            Assert.Equal(correction, plan!.CorrectedQuery);
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
            Assert.Equal("clinical attachment ext:pdf", plan!.CorrectedQuery);
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
            // The raw query matched something (zero-result gating happens above the planner), so a
            // "correction" that equals the original term must never be planned.
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
