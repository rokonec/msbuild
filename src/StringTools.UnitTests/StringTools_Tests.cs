// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#if NET35_UNITTEST
extern alias StringToolsNet35;
#endif

using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

using Shouldly;
using Xunit;

#if NET35_UNITTEST
using StringToolsNet35::Microsoft.StringTools;
using Shouldly.Configuration;
#else
using Microsoft.StringTools;
#endif

namespace Microsoft.StringTools.Tests
{
    public class StringTools_Tests
    {
        private const string HardcodedInternedString = "Hello";
        private const string HardcodedNonInternedString = "World";

        public StringTools_Tests()
        {
            Strings.ResetForTests();
        }

        [Fact]
        public void CallsRegisteredStringInterningCallback()
        {
            int callbackCallCounter = 0;

            TryInternStringDelegate callback = (ref InternableString internableString, out string result) =>
            {
                callbackCallCounter++;
                if (internableString.StartsWithStringByOrdinalComparison(HardcodedInternedString))
                {
                    result = HardcodedInternedString;
                    return true;
                }
                if (internableString.StartsWithStringByOrdinalComparison(HardcodedNonInternedString))
                {
                    result = null;
                    return true;
                }
                result = null;
                return false;
            };

            Strings.RegisterStringInterningCallback(callback);

            InternableString internedString = new InternableString(new string(HardcodedInternedString.ToCharArray()));
            internedString.ToString().ShouldBeSameAs(HardcodedInternedString);

            InternableString nonInternedString = new InternableString(new string(HardcodedNonInternedString.ToCharArray()));
            nonInternedString.ToString().ShouldBe(HardcodedNonInternedString);
            nonInternedString.ToString().ShouldNotBeSameAs(HardcodedNonInternedString);

            Strings.UnregisterStringInterningCallback(callback);

            callbackCallCounter.ShouldBe(3);
        }

        [Fact]
        public void DoesNotCallUnregisteredStringInterningCallback()
        {
            int callbackCallCounter = 0;

            TryInternStringDelegate callback = (ref InternableString internableString, out string result) =>
            {
                callbackCallCounter++;
                result = null;
                return false;
            };

            Strings.RegisterStringInterningCallback(callback);
            Strings.UnregisterStringInterningCallback(callback);

            InternableString internedString = new InternableString(new string(HardcodedInternedString.ToCharArray()));
            internedString.ToString().ShouldBe(HardcodedInternedString);
            internedString.ToString().ShouldNotBeSameAs(HardcodedInternedString);

            callbackCallCounter.ShouldBe(0);
        }

        [Fact]
        public void CreatesDiagnosticReport()
        {
            string statisticsNotEnabledString = "EnableStatisticsGathering() has not been called";

            Strings.CreateDiagnosticReport().ShouldContain(statisticsNotEnabledString);

            Strings.EnableDiagnostics();
            string report = Strings.CreateDiagnosticReport();

            report.ShouldNotContain(statisticsNotEnabledString);
            report.ShouldContain("Eliminated Strings");
        }
    }
}
