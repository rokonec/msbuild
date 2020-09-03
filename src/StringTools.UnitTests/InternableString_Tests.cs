// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;

using Shouldly;
using Xunit;

namespace StringTools.Tests
{
    public class InternableString_Tests
    {
        private InternableString MakeInternableString(InterningTestData.TestDatum datum, bool appendSubStrings = false)
        {
            bool wrapFirstFragment = datum.Fragments.Length > 0 && datum.Fragments[0] != null;

            InternableString internableString = wrapFirstFragment
                ? new InternableString(datum.Fragments[0])
                : new InternableString();

            for (int i = 1; i < datum.Fragments.Length; i++)
            {
                if (appendSubStrings)
                {
                    int index = datum.Fragments[i].Length / 2;
                    internableString.Append(datum.Fragments[i], 0, index);
                    internableString.Append(datum.Fragments[i], index, datum.Fragments[i].Length - index);
                }
                else
                {
                    internableString.Append(datum.Fragments[i]);
                }
            }
            return internableString;
        }

        public static IEnumerable<object[]> TestData => InterningTestData.TestData;
        public static IEnumerable<object[]> TestDataForTrim => InterningTestData.TestDataForTrim;

        [Theory]
        [MemberData(nameof(TestData))]
        public void LengthReturnsLength(InterningTestData.TestDatum datum)
        {
            MakeInternableString(datum).Length.ShouldBe(datum.Length);
        }

        [Theory]
        [MemberData(nameof(TestData))]
        public void EnumeratorEnumeratesCharacters(InterningTestData.TestDatum datum)
        {
            InternableString internableString = MakeInternableString(datum);
            int index = 0;
            foreach (char ch in internableString)
            {
                ch.ShouldBe(datum[index]);
                index++;
            }
        }

        [Theory]
        [MemberData(nameof(TestData))]
        public void IndexerReturnsCharacters(InterningTestData.TestDatum datum)
        {
            InternableString internableString = MakeInternableString(datum);
            int length = datum.Length;
            for (int index = 0; index < length; index++)
            {
                internableString[index].ShouldBe(datum[index]);
            }
        }

        [Theory]
        [MemberData(nameof(TestData))]
        public void StartsWithStringByOrdinalComparisonReturnsExpectedValue(InterningTestData.TestDatum datum)
        {
            InternableString internableString = MakeInternableString(datum);
            internableString.StartsWithStringByOrdinalComparison(string.Empty).ShouldBeTrue();

            string substr = datum.Fragments[0] ?? string.Empty;
            internableString.StartsWithStringByOrdinalComparison(substr).ShouldBeTrue();
            internableString.StartsWithStringByOrdinalComparison(substr.Substring(0, substr.Length / 2)).ShouldBeTrue();

            if (datum.Fragments.Length > 1)
            {
                substr += datum.Fragments[1];
                internableString.StartsWithStringByOrdinalComparison(substr).ShouldBeTrue();
                internableString.StartsWithStringByOrdinalComparison(substr.Substring(0, substr.Length - datum.Fragments[1].Length / 2)).ShouldBeTrue();

                internableString.StartsWithStringByOrdinalComparison(datum.ToString()).ShouldBeTrue();
            }

            internableString.StartsWithStringByOrdinalComparison("Things").ShouldBeFalse();
        }

        [Fact]
        public void ReferenceEqualsReturnsExpectedValue()
        {
            string str = "Test";
            InternableString internableString = new InternableString(str);
            internableString.ReferenceEquals(str).ShouldBeTrue();
            internableString.Append("Things");
            internableString.ReferenceEquals(str).ShouldBeFalse();
        }

        [Theory]
        [MemberData(nameof(TestData))]
        public void AppendAppendsString(InterningTestData.TestDatum datum)
        {
            MakeInternableString(datum, false).ExpensiveConvertToString().ShouldBe(datum.ToString());
        }

        [Theory]
        [MemberData(nameof(TestData))]
        public void AppendAppendsSubstring(InterningTestData.TestDatum datum)
        {
            MakeInternableString(datum, true).ExpensiveConvertToString().ShouldBe(datum.ToString());
        }

        [Theory]
        [MemberData(nameof(TestDataForTrim))]
        public void TrimStartRemovesLeadingWhiteSpace(InterningTestData.TestDatum datum)
        {
            InternableString internableString = MakeInternableString(datum);
            internableString.TrimStart();
            internableString.ExpensiveConvertToString().ShouldBe(datum.ToString().TrimStart());
        }

        [Theory]
        [MemberData(nameof(TestDataForTrim))]
        public void TrimEndRemovesTrailingWhiteSpace(InterningTestData.TestDatum datum)
        {
            InternableString internableString = MakeInternableString(datum);
            internableString.TrimEnd();
            internableString.ExpensiveConvertToString().ShouldBe(datum.ToString().TrimEnd());
        }

        [Theory]
        [MemberData(nameof(TestDataForTrim))]
        public void TrimRemovesLeadingAndTrailingWhiteSpace(InterningTestData.TestDatum datum)
        {
            InternableString internableString = MakeInternableString(datum);
            internableString.Trim();
            internableString.ExpensiveConvertToString().ShouldBe(datum.ToString().Trim());
        }

        [Theory]
        [MemberData(nameof(TestData))]
        public void ClearRemovesAllCharacters(InterningTestData.TestDatum datum)
        {
            InternableString internableString = MakeInternableString(datum);
            internableString.Clear();
            internableString.Length.ShouldBe(0);
            internableString.GetEnumerator().MoveNext().ShouldBeFalse();
        }

        [Fact]
        public void ExpensiveConvertToStringRoundtrips()
        {
            string str = "Test";
            InternableString internableString = new InternableString(str);
            internableString.ExpensiveConvertToString().ShouldBeSameAs(str);
        }
    }
}
