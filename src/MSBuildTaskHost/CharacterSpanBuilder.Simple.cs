// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.Build.Shared;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Text;

namespace System
{
    internal static class MemoryExtensions
    {
        public static string AsSpan<T>(this T[] array, int start, int length)
        {
            return null;
        }
    }
}

namespace Microsoft.Build.Shared
{
    /// <summary>
    /// 
    /// </summary>
    internal ref struct CharacterSpanBuilder
    {
        StringBuilder _builder;
        string _firstString;

        /// <summary>
        /// </summary>
        internal CharacterSpanBuilder(int capacity = 4)
        {
            _builder = new StringBuilder(capacity * 128);
            _firstString = null;
        }

        internal CharacterSpanBuilder(string str)
        {
            _builder = new StringBuilder(str);
            _firstString = str;
        }

        /// <summary>
        /// The length of the target.
        /// </summary>
        public int Length => _builder.Length;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="index"></param>
        /// <returns></returns>
        public char this[int index] => _builder[index];

        public bool StartsWithStringByOrdinalComparison(string other)
        {
            if (Length < other.Length)
            {
                return false;
            }
            for (int i = 0; i < other.Length; i++)
            {
                if (other[i] != this[i])
                {
                    return false;
                }
            }
            return true;
        }

        public string ExpensiveConvertToString()
        {
            return ToString();
            }

        public bool ReferenceEquals(string str)
        {
            return Object.ReferenceEquals(str, _firstString);
        }

        /// <summary>
        /// Convert to a string.
        /// </summary>
        public override unsafe string ToString()
        {
            // Special case: if we hold just one string, we can directly return it.
            if (_firstString != null && _firstString.Length == Length)
            {
                return _firstString;
            }
            return _builder.ToString();
        }

        /// <summary>
        /// Append a string.
        /// </summary>
        internal CharacterSpanBuilder Append(string value)
        {
            _builder.Append(value);
            return this;
        }

        /// <summary>
        /// Append a substring.
        /// </summary>
        internal CharacterSpanBuilder Append(string value, int startIndex, int count)
        {
            _builder.Append(value, startIndex, count);
            return this;
        }

        internal CharacterSpanBuilder Append(char[] chars, int startIndex, int count)
        {
            _builder.Append(chars, startIndex, count);
            return this;
        }

        public CharacterSpanBuilder Clear()
        {
            _builder.Length = 0;
            _firstString = null;
            return this;
        }
    }
}
