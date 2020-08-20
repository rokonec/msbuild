// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;

namespace Microsoft.Build.Shared
{
    /// <summary>
    /// 
    /// </summary>
    internal ref struct CharacterSpanBuilder
    {
        private ReadOnlySpan<char> _firstSpan;

        private List<ReadOnlyMemory<char>> _additionalSpans;

#if NETCOREAPP
        private string _firstString;
#endif
        /// <summary>
        /// </summary>
        internal CharacterSpanBuilder(int capacity = 4)
        {
            _firstSpan = default(ReadOnlySpan<char>);
            _additionalSpans = new List<ReadOnlyMemory<char>>(capacity);
            Length = 0;
#if NETCOREAPP
            _firstString = null;
#endif
        }

        internal CharacterSpanBuilder(ReadOnlySpan<char> span)
        {
            _firstSpan = span;
            _additionalSpans = null;
            Length = span.Length;
#if NETCOREAPP
            _firstString = null;
#endif
        }

        internal CharacterSpanBuilder(string str)
        {
            _firstSpan = str.AsSpan();
            _additionalSpans = null;
            Length = str.Length;
#if NETCOREAPP
            _firstString = str;
#endif
        }

        /// <summary>
        /// The length of the target.
        /// </summary>
        public int Length { get; private set; }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="index"></param>
        /// <returns></returns>
        public char this[int index]
        {
            get
            {
                ErrorUtilities.VerifyThrowArgumentOutOfRange(index >= 0 && index < Length, nameof(index));

                if (index < _firstSpan.Length)
                {
                    return _firstSpan[index];
                }
                index -= _firstSpan.Length;

                foreach (ReadOnlyMemory<char> span in _additionalSpans)
                {
                    if (index < span.Length)
                    {
                        return span.Span[index];
                    }
                    index -= span.Length;
                }

                ErrorUtilities.ThrowInternalError("Inconsistent {0} state", nameof(CharacterSpanBuilder));
                throw new InvalidOperationException();
            }
        }

        public bool StartsWithStringByOrdinalComparison(string other)
        {
            int otherLength = other.Length;
            if (otherLength > Length)
            {
                // Can't start with a string which is longer.
                return false;
            }

            if (otherLength <= _firstSpan.Length)
            {
                return _firstSpan.StartsWith(other.AsSpan());
            }
            if (_firstSpan.CompareTo(other.AsSpan(0, _firstSpan.Length), StringComparison.Ordinal) != 0)
            {
                return false;
            }

            if (_additionalSpans != null)
            {
                int otherStart = _firstSpan.Length;
                foreach (ReadOnlyMemory<char> span in _additionalSpans)
                {
                    if (otherLength - otherStart <= span.Length)
                    {
                        return span.Span.StartsWith(other.AsSpan(otherStart, otherLength - otherStart));
                    }
                    if (span.Span.CompareTo(other.AsSpan(otherStart, span.Length), StringComparison.Ordinal) != 0)
                    {
                        return false;
                    }
                    otherStart += span.Length;
                }
            }

            ErrorUtilities.ThrowInternalError("Inconsistent {0} state", nameof(CharacterSpanBuilder));
            throw new InvalidOperationException();
        }

        public string ExpensiveConvertToString()
        {
            return ToString();
        }

        public bool ReferenceEquals(string str)
        {
            if (_firstSpan.Length == Length)
            {
                return _firstSpan == str.AsSpan();
            }
            if (_firstSpan.IsEmpty && _additionalSpans.Count == 1 && _additionalSpans[0].Length == Length)
            {
                return _additionalSpans[0].Span == str.AsSpan();
            }
            return false;
        }

        /// <summary>
        /// Convert to a string.
        /// </summary>
        public override unsafe string ToString()
        {
            // Special case: if we hold just one string, we can directly return it.
            if (Length == 0)
            {
                return string.Empty;
            }
            if (_firstSpan.Length == Length)
            {
#if NETCOREAPP
                if (_firstString != null)
                {
                    return _firstString;
                }
#else
                return _firstSpan.ToString();
#endif
            }
            if (_firstSpan.IsEmpty && _additionalSpans[0].Length == Length)
            {
                return _additionalSpans[0].ToString();
            }

            // In all other cases we create a new string instance and concatenate all spans into it.
            string result = new string((char)0, Length);
            int charsRemaining = Length;

            fixed (char* resultPtr = result)
            {
                char* destPtr = resultPtr;
                if (_firstSpan.Length > 0)
                {
                    _firstSpan.CopyTo(new Span<char>(destPtr, charsRemaining));
                    destPtr += _firstSpan.Length;
                    charsRemaining -= _firstSpan.Length;
                }

                if (_additionalSpans != null)
                {
                    foreach (ReadOnlyMemory<char> span in _additionalSpans)
                    {
                        span.Span.CopyTo(new Span<char>(destPtr, charsRemaining));
                        destPtr += span.Length;
                        charsRemaining -= span.Length;
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// Append a string.
        /// </summary>
        internal CharacterSpanBuilder Append(string value)
        {
            AddSpan(value.AsMemory());
            return this;
        }

        /// <summary>
        /// Append a substring.
        /// </summary>
        internal CharacterSpanBuilder Append(string value, int startIndex, int count)
        {
            AddSpan(value.AsMemory(startIndex, count));
            return this;
        }

        internal CharacterSpanBuilder Append(char[] chars, int startIndex, int count)
        {
            AddSpan(chars.AsMemory(startIndex, count));
            return this;
        }

        public CharacterSpanBuilder Clear()
        {
            _firstSpan = default(ReadOnlySpan<char>);
            _additionalSpans?.Clear();
            Length = 0;
#if NETCOREAPP
            _firstString = null;
#endif
            return this;
        }

        private void AddSpan(ReadOnlyMemory<char> span)
        {
            _additionalSpans ??= new List<ReadOnlyMemory<char>>();
            _additionalSpans.Add(span);
            Length += span.Length;
        }
    }
}
