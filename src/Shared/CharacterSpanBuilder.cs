// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections;
using System.Collections.Generic;

namespace Microsoft.Build.Shared
{
    /// <summary>
    /// 
    /// </summary>
    internal ref struct CharacterSpanBuilder
    {
        public ref struct Enumerator
        {
            private readonly CharacterSpanBuilder _spanBuilder;
            private int _spanIndex;
            private int _charIndex;

            public Enumerator(CharacterSpanBuilder spanBuilder)
            {
                _spanBuilder = spanBuilder;
                _spanIndex = -1;
                _charIndex = -1;
            }

            public ref readonly char Current
            {
                get
                {
                    if (_spanIndex == -1)
                    {
                        return ref _spanBuilder._firstSpan[_charIndex];
                    }
                    ReadOnlyMemory<char> span = _spanBuilder._additionalSpans[_spanIndex];
                    return ref span.Span[_charIndex];
                }
            }

            public bool MoveNext()
            {
                int newIndex = _charIndex + 1;
                if (_spanIndex == -1)
                {
                    if (newIndex < _spanBuilder._firstSpan.Length)
                    {
                        _charIndex = newIndex;
                        return true;
                    }
                    _spanIndex = 0;
                    newIndex = 0;
                }

                if (_spanBuilder._additionalSpans != null)
                {
                    while (_spanIndex < _spanBuilder._additionalSpans.Count)
                    {
                        if (newIndex < _spanBuilder._additionalSpans[_spanIndex].Length)
                        {
                            _charIndex = newIndex;
                            return true;
                        }
                        _spanIndex++;
                        newIndex = 0;
                    }
                }
                return false;
            }
        }

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

        public Enumerator GetEnumerator()
        {
            return new Enumerator(this);
        }

        /// <summary>
        /// Note, O(N), use enumeration for O(1)-per-character scan.
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
        internal void Append(string value)
        {
            AddSpan(value.AsMemory());
        }

        /// <summary>
        /// Append a substring.
        /// </summary>
        internal void Append(string value, int startIndex, int count)
        {
            AddSpan(value.AsMemory(startIndex, count));
        }

        internal void TrimStart()
        {
            int oldLength = _firstSpan.Length;
            _firstSpan = _firstSpan.TrimStart();
            Length -= (oldLength - _firstSpan.Length);

            if (_firstSpan.IsEmpty && _additionalSpans != null)
            {
                for (int spanIdx = 0; spanIdx < _additionalSpans.Count; spanIdx++)
                {
                    ReadOnlySpan<char> span = _additionalSpans[spanIdx].Span;
                    int i = 0;
                    while (i < span.Length && char.IsWhiteSpace(span[i]))
                    {
                        i++;
                    }
                    if (i > 0)
                    {
                        _additionalSpans[spanIdx] = _additionalSpans[spanIdx].Slice(i);
                        Length -= i;
                    }
                    if (!_additionalSpans[spanIdx].IsEmpty)
                    {
                        return;
                    }
                }
            }
        }

        internal void TrimEnd()
        {
            if (_additionalSpans != null)
            {
                for (int spanIdx = _additionalSpans.Count - 1; spanIdx >= 0; spanIdx--)
                {
                    ReadOnlySpan<char> span = _additionalSpans[spanIdx].Span;
                    int i = span.Length - 1;
                    while (i >= 0 && char.IsWhiteSpace(span[i]))
                    {
                        i--;
                    }
                    if (i + 1 < span.Length)
                    {
                        _additionalSpans[spanIdx] = _additionalSpans[spanIdx].Slice(0, i + 1);
                        Length -= span.Length - (i + 1);
                    }
                    if (!_additionalSpans[spanIdx].IsEmpty)
                    {
                        return;
                    }
                }
            }

            int oldLength = _firstSpan.Length;
            _firstSpan = _firstSpan.TrimEnd();
            Length -= (oldLength - _firstSpan.Length);
        }

        internal void Trim()
        {
            TrimStart();
            TrimEnd();
        }

        public void Clear()
        {
            _firstSpan = default(ReadOnlySpan<char>);
            _additionalSpans?.Clear();
            Length = 0;
#if NETCOREAPP
            _firstString = null;
#endif
        }

        private void AddSpan(ReadOnlyMemory<char> span)
        {
            _additionalSpans ??= new List<ReadOnlyMemory<char>>();
            _additionalSpans.Add(span);
            Length += span.Length;
        }
    }
}
