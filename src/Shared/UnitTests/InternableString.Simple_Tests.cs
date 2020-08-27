// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

extern alias MSBuildTaskHost;

using System;
using System.Collections.Generic;
using System.Linq;

using Shouldly;
using Xunit;

using InternableString = MSBuildTaskHost::Microsoft.Build.Shared.InternableString;

namespace Microsoft.Build.UnitTests
{
    /// <summary>
    /// These tests are running against the .NET 3.5 implementation in src\MSBuildTaskHost\InternableString.Simple.cs
    /// </summary>
    public class InternableString_Simple_Tests
    {
        /// <summary>
        /// Represents an array of string fragments to create the InternableString from.
        /// </summary>
        public class TestDatum
        {
            public string[] Fragments { get; }

            public int Length => Enumerable.Sum(Fragments, (string fragment) => fragment?.Length ?? 0);

            public TestDatum(params string[] fragments)
            {
                Fragments = fragments;
            }

            public char this[int index]
            {
                get
                {
                    foreach (string fragment in Fragments)
                    {
                        if (fragment != null)
                        {
                            if (index < fragment.Length)
                            {
                                return fragment[index];
                            }
                            index -= fragment.Length;
                        }
                    }
                    throw new InvalidOperationException();
                }
            }

            internal InternableString MakeInternableString(bool appendSubStrings = false)
            {
                bool wrapFirstFragment = Fragments.Length > 0 && Fragments[0] != null;

                InternableString internableString = wrapFirstFragment
                    ? new InternableString(Fragments[0])
                    : new InternableString();

                for (int i = 1; i < Fragments.Length; i++)
                {
                    if (appendSubStrings)
                    {
                        int index = Fragments[i].Length / 2;
                        internableString.Append(Fragments[i], 0, index);
                        internableString.Append(Fragments[i], index, Fragments[i].Length - index);
                    }
                    else
                    {
                        internableString.Append(Fragments[i]);
                    }
                }
                return internableString;
            }

            public override string ToString()
            {
                return string.Join(string.Empty, Fragments);
            }
        }

        public static IEnumerable<object[]> TestData
        {
            get
            {
                yield return new object[] { new TestDatum((string)null) };
                yield return new object[] { new TestDatum("") };
                yield return new object[] { new TestDatum("Test") };
                yield return new object[] { new TestDatum(null, "All") };
                yield return new object[] { new TestDatum("", "All") };
                yield return new object[] { new TestDatum("Test", "All", "The", "Things") };
            }
        }

        [Theory]
        [MemberData(nameof(TestData))]
        public void LengthReturnsLength(TestDatum datum)
        {
            datum.MakeInternableString().Length.ShouldBe(datum.Length);
        }

        [Theory]
        [MemberData(nameof(TestData))]
        public void EnumeratorEnumeratesCharacters(TestDatum datum)
        {
            InternableString internableString = datum.MakeInternableString();
            int index = 0;
            foreach (char ch in internableString)
            {
                ch.ShouldBe(datum[index]);
                index++;
            }
        }

        [Theory]
        [MemberData(nameof(TestData))]
        public void IndexerReturnsCharacters(TestDatum datum)
        {
            InternableString internableString = datum.MakeInternableString();
            int length = datum.Length;
            for (int index = 0; index < length; index++)
            {
                internableString[index].ShouldBe(datum[index]);
            }
        }

        [Theory]
        [MemberData(nameof(TestData))]
        public void StartsWithStringByOrdinalComparisonReturnsExpectedValue(TestDatum datum)
        {
            InternableString internableString = datum.MakeInternableString();
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
        public void AppendAppendsString(TestDatum datum)
        {
            datum.MakeInternableString(false).ExpensiveConvertToString().ShouldBe(datum.ToString());
        }

        [Theory]
        [MemberData(nameof(TestData))]
        public void AppendAppendsSubstring(TestDatum datum)
        {
            datum.MakeInternableString(true).ExpensiveConvertToString().ShouldBe(datum.ToString());
        }

        [Theory]
        [MemberData(nameof(TestData))]
        public void ClearRemovesAllCharacters(TestDatum datum)
        {
            InternableString internableString = datum.MakeInternableString();
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
