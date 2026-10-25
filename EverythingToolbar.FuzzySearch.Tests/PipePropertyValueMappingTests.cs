using System;
using EverythingToolbar.Platform.Search;
using Xunit;

namespace EverythingToolbar.FuzzySearch.Tests
{
    public class PipePropertyValueMappingTests
    {
        public class TextFromNativeLength
        {
            [Fact]
            public void NonEmptyLengthReturnsBufferContents()
            {
                var result = EverythingPipeClient.TextFromNativeLength(4, 4096, () => ".txt");

                Assert.Equal(".txt", result);
            }

            [Fact]
            public void TruncatesToReportedLength()
            {
                var result = EverythingPipeClient.TextFromNativeLength(3, 4096, () => ".txt");

                Assert.Equal(".tx", result);
            }

            [Fact]
            public void ZeroLengthYieldsNull()
            {
                var result = EverythingPipeClient.TextFromNativeLength(0, 4096, () => ".txt");

                Assert.Null(result);
            }

            [Fact]
            public void EmptyBufferYieldsNull()
            {
                var result = EverythingPipeClient.TextFromNativeLength(4, 0, () => ".txt");

                Assert.Null(result);
            }

            [Fact]
            public void LengthBeyondBufferIsClampedToBufferCapacity()
            {
                // A bogus native length must not read past the buffer (4096 chars capacity
                // means at most 4095 chars are valid).
                var result = EverythingPipeClient.TextFromNativeLength(10000, 4096, () => "short");

                Assert.Equal("short", result);
            }
        }

        public class DateCreatedFromNative
        {
            [Fact]
            public void ValidFileTimeConvertsToUtcLocalDateTime()
            {
                var expected = new DateTime(2024, 6, 15, 12, 0, 0);
                var fileTime = expected.ToFileTime();

                var result = EverythingPipeClient.DateCreatedFromNative((ulong)fileTime);

                Assert.Equal(expected, result);
            }

            [Fact]
            public void UInt64MaxYieldsNull()
            {
                // EVERYTHING3_UINT64_MAX signals an unavailable property.
                var result = EverythingPipeClient.DateCreatedFromNative(ulong.MaxValue);

                Assert.Null(result);
            }

            [Fact]
            public void ZeroYieldsNull()
            {
                var result = EverythingPipeClient.DateCreatedFromNative(0);

                Assert.Null(result);
            }
        }

        public class AttributesFromNative
        {
            [Fact]
            public void NormalValueIsPassedThrough()
            {
                const uint readOnly = 0x1; // FILE_ATTRIBUTE_READONLY

                var result = EverythingPipeClient.AttributesFromNative(readOnly);

                Assert.Equal(readOnly, result);
            }

            [Fact]
            public void DwordMaxYieldsNull()
            {
                // EVERYTHING3_DWORD_MAX signals an unavailable property / not-found error.
                var result = EverythingPipeClient.AttributesFromNative(uint.MaxValue);

                Assert.Null(result);
            }

            [Fact]
            public void ZeroIsPreserved()
            {
                var result = EverythingPipeClient.AttributesFromNative(0);

                Assert.Equal(0u, result);
            }
        }
    }
}
