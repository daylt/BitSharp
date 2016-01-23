﻿using BitSharp.Common.ExtensionMethods;
using System;
using System.Data.HashFunction;
using System.Globalization;
using System.Net;
using System.Numerics;

namespace BitSharp.Common
{
    public class UInt256 : IComparable<UInt256>
    {
        public static UInt256 Zero { get; } = new UInt256(new byte[0]);
        public static UInt256 One { get; } = (UInt256)1;

        // parts are big-endian
        private readonly UInt64 part1;
        private readonly UInt64 part2;
        private readonly UInt64 part3;
        private readonly UInt64 part4;
        private readonly int hashCode;

        public UInt256(byte[] value)
        {
            if (value.Length > 32 && !(value.Length == 33 && value[32] == 0))
                throw new ArgumentOutOfRangeException();

            if (value.Length < 32)
                value = value.Concat(new byte[32 - value.Length]);

            // convert parts and store
            this.part1 = Bits.ToUInt64(value, 24);
            this.part2 = Bits.ToUInt64(value, 16);
            this.part3 = Bits.ToUInt64(value, 8);
            this.part4 = Bits.ToUInt64(value, 0);

            this.hashCode = Bits.ToInt32(new xxHash(32).ComputeHash(value));
        }

        public UInt256(byte[] value, int offset)
        {
            // convert parts and store
            this.part1 = Bits.ToUInt64(value, offset + 24);
            this.part2 = Bits.ToUInt64(value, offset + 16);
            this.part3 = Bits.ToUInt64(value, offset + 8);
            this.part4 = Bits.ToUInt64(value, offset + 0);

            var hashBytes = ToByteArray();
            this.hashCode = Bits.ToInt32(new xxHash(32).ComputeHash(hashBytes));
        }

        private UInt256(UInt64 part1, UInt64 part2, UInt64 part3, UInt64 part4)
        {
            this.part1 = part1;
            this.part2 = part2;
            this.part3 = part3;
            this.part4 = part4;

            var hashBytes = ToByteArray();
            this.hashCode = Bits.ToInt32(new xxHash(32).ComputeHash(hashBytes));
        }

        public UInt256(int value)
            : this(Bits.GetBytes(value))
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException();
        }

        public UInt256(long value)
            : this(Bits.GetBytes(value))
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException();
        }

        public UInt256(uint value)
            : this(Bits.GetBytes(value))
        { }

        public UInt256(ulong value)
            : this(Bits.GetBytes(value))
        { }

        public UInt256(BigInteger value)
            : this(value.ToByteArray())
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException();
        }

        public UInt64 Part1 => part1;
        public UInt64 Part2 => part2;
        public UInt64 Part3 => part3;
        public UInt64 Part4 => part4;

        public byte[] ToByteArray()
        {
            var buffer = new byte[32];
            ToByteArray(buffer);
            return buffer;
        }

        public void ToByteArray(byte[] buffer, int offset = 0)
        {
            Bits.EncodeUInt64(part4, buffer, 0 + offset);
            Bits.EncodeUInt64(part3, buffer, 8 + offset);
            Bits.EncodeUInt64(part2, buffer, 16 + offset);
            Bits.EncodeUInt64(part1, buffer, 24 + offset);
        }

        public byte[] ToByteArrayBE()
        {
            var buffer = new byte[32];
            ToByteArrayBE(buffer);
            return buffer;
        }

        public void ToByteArrayBE(byte[] buffer, int offset = 0)
        {
            Bits.EncodeUInt64BE(part1, buffer, 0 + offset);
            Bits.EncodeUInt64BE(part2, buffer, 8 + offset);
            Bits.EncodeUInt64BE(part3, buffer, 16 + offset);
            Bits.EncodeUInt64BE(part4, buffer, 24 + offset);
        }

        //TODO properly taken into account host endianness
        public static UInt256 FromByteArrayBE(byte[] buffer, int offset = 0)
        {
            unchecked
            {
                if (buffer.Length < offset + 32)
                    throw new ArgumentException();

                var part1 = (ulong)IPAddress.HostToNetworkOrder(BitConverter.ToInt64(buffer, 0 + offset));
                var part2 = (ulong)IPAddress.HostToNetworkOrder(BitConverter.ToInt64(buffer, 8 + offset));
                var part3 = (ulong)IPAddress.HostToNetworkOrder(BitConverter.ToInt64(buffer, 16 + offset));
                var part4 = (ulong)IPAddress.HostToNetworkOrder(BitConverter.ToInt64(buffer, 24 + offset));

                return new UInt256(part1, part2, part3, part4);
            }
        }

        public BigInteger ToBigInteger()
        {
            // add a trailing zero so that value is always positive
            var buffer = new byte[33];
            ToByteArray(buffer);
            return new BigInteger(buffer);
        }

        public int CompareTo(UInt256 other)
        {
            if (this == other)
                return 0;
            else if (this < other)
                return -1;
            else if (this > other)
                return +1;

            throw new Exception();
        }

        public static explicit operator BigInteger(UInt256 value)
        {
            return value.ToBigInteger();
        }

        public static explicit operator UInt256(byte value)
        {
            return new UInt256(value);
        }

        public static explicit operator UInt256(int value)
        {
            return new UInt256(value);
        }

        public static explicit operator UInt256(long value)
        {
            return new UInt256(value);
        }

        public static explicit operator UInt256(sbyte value)
        {
            return new UInt256(value);
        }

        public static explicit operator UInt256(short value)
        {
            return new UInt256(value);
        }

        public static explicit operator UInt256(uint value)
        {
            return new UInt256(value);
        }

        public static explicit operator UInt256(ulong value)
        {
            return new UInt256(value);
        }

        public static explicit operator UInt256(ushort value)
        {
            return new UInt256(value);
        }

        public static bool operator ==(UInt256 left, UInt256 right)
        {
            return object.ReferenceEquals(left, right) || (!object.ReferenceEquals(left, null) && !object.ReferenceEquals(right, null) && left.part1 == right.part1 && left.part2 == right.part2 && left.part3 == right.part3 && left.part4 == right.part4);
        }

        public static bool operator !=(UInt256 left, UInt256 right)
        {
            return !(left == right);
        }

        public static bool operator <(UInt256 left, UInt256 right)
        {
            if (left.part1 < right.part1)
                return true;
            else if (left.part1 == right.part1 && left.part2 < right.part2)
                return true;
            else if (left.part1 == right.part1 && left.part2 == right.part2 && left.part3 < right.part3)
                return true;
            else if (left.part1 == right.part1 && left.part2 == right.part2 && left.part3 == right.part3 && left.part4 < right.part4)
                return true;

            return false;
        }

        public static bool operator <=(UInt256 left, UInt256 right)
        {
            if (left.part1 < right.part1)
                return true;
            else if (left.part1 == right.part1 && left.part2 < right.part2)
                return true;
            else if (left.part1 == right.part1 && left.part2 == right.part2 && left.part3 < right.part3)
                return true;
            else if (left.part1 == right.part1 && left.part2 == right.part2 && left.part3 == right.part3 && left.part4 < right.part4)
                return true;

            return left == right;
        }

        public static bool operator >(UInt256 left, UInt256 right)
        {
            if (left.part1 > right.part1)
                return true;
            else if (left.part1 == right.part1 && left.part2 > right.part2)
                return true;
            else if (left.part1 == right.part1 && left.part2 == right.part2 && left.part3 > right.part3)
                return true;
            else if (left.part1 == right.part1 && left.part2 == right.part2 && left.part3 == right.part3 && left.part4 > right.part4)
                return true;

            return false;
        }

        public static bool operator >=(UInt256 left, UInt256 right)
        {
            if (left.part1 > right.part1)
                return true;
            else if (left.part1 == right.part1 && left.part2 > right.part2)
                return true;
            else if (left.part1 == right.part1 && left.part2 == right.part2 && left.part3 > right.part3)
                return true;
            else if (left.part1 == right.part1 && left.part2 == right.part2 && left.part3 == right.part3 && left.part4 > right.part4)
                return true;

            return left == right;
        }

        // TODO doesn't compare against other numerics
        public override bool Equals(object obj)
        {
            if (!(obj is UInt256))
                return false;

            var other = (UInt256)obj;
            return other.part1 == this.part1 && other.part2 == this.part2 && other.part3 == this.part3 && other.part4 == this.part4;
        }

        public override int GetHashCode() => hashCode;

        public override string ToString()
        {
            return this.ToHexNumberString();
        }

        public static UInt256 Parse(string value)
        {
            return new UInt256(BigInteger.Parse("0" + value).ToByteArray());
        }

        public static UInt256 Parse(string value, IFormatProvider provider)
        {
            return new UInt256(BigInteger.Parse("0" + value, provider).ToByteArray());
        }

        public static UInt256 Parse(string value, NumberStyles style)
        {
            return new UInt256(BigInteger.Parse("0" + value, style).ToByteArray());
        }

        public static UInt256 Parse(string value, NumberStyles style, IFormatProvider provider)
        {
            return new UInt256(BigInteger.Parse("0" + value, style, provider).ToByteArray());
        }

        public static UInt256 ParseHex(string value)
        {
            return new UInt256(BigInteger.Parse("0" + value, NumberStyles.HexNumber).ToByteArray());
        }

        public static double Log(UInt256 value, double baseValue)
        {
            return BigInteger.Log(value.ToBigInteger(), baseValue);
        }

        public static UInt256 operator %(UInt256 dividend, UInt256 divisor)
        {
            return new UInt256(dividend.ToBigInteger() % divisor.ToBigInteger());
        }

        public static UInt256 Pow(UInt256 value, int exponent)
        {
            return new UInt256(BigInteger.Pow(value.ToBigInteger(), exponent));
        }

        public static UInt256 operator *(UInt256 left, UInt256 right)
        {
            return new UInt256(left.ToBigInteger() * right.ToBigInteger());
        }

        public static UInt256 operator >>(UInt256 value, int shift)
        {
            return new UInt256(value.ToBigInteger() >> shift);
        }

        public static UInt256 operator /(UInt256 dividend, UInt256 divisor)
        {
            return new UInt256(dividend.ToBigInteger() / divisor.ToBigInteger());
        }

        public static UInt256 operator ~(UInt256 value)
        {
            return new UInt256(~value.part1, ~value.part2, ~value.part3, ~value.part4);
        }

        public static UInt256 DivRem(UInt256 dividend, UInt256 divisor, out UInt256 remainder)
        {
            BigInteger remainderBigInt;
            var result = new UInt256(BigInteger.DivRem(dividend.ToBigInteger(), divisor.ToBigInteger(), out remainderBigInt));
            remainder = new UInt256(remainderBigInt);
            return result;
        }

        public static explicit operator double(UInt256 value)
        {
            return (double)value.ToBigInteger();
        }
    }
}