﻿using BitSharp.Common;
using BitSharp.Common.ExtensionMethods;
using BitSharp.Storage;
using BitSharp.Network;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BitSharp.Data;
using System.Data.SqlClient;
using System.Collections.Immutable;
using System.IO;

namespace BitSharp.Storage.Esent
{
    public class BlockTransactionsStorage : EsentDataStorage, IBlockTransactionsStorage
    {
        public BlockTransactionsStorage(EsentStorageContext storageContext)
            : base(storageContext, "blockTxHashes")
        { }

        public IEnumerable<UInt256> ReadAllKeys()
        {
            return this.Data.Keys.Select(x => new UInt256(x));
        }

        public IEnumerable<KeyValuePair<UInt256, ImmutableList<UInt256>>> ReadAllValues()
        {
            return this.Data.Select(x =>
                {
                    var blockHash = new UInt256(x.Key);
                    var txHashesBytes = x.Value;

                    var txHashes = ImmutableList.CreateBuilder<UInt256>();
                    var txHashBytes = new byte[32];
                    for (var i = 0; i < txHashesBytes.Length; i += 32)
                    {
                        Buffer.BlockCopy(txHashesBytes, i, txHashBytes, 0, 32);
                        txHashes.Add(new UInt256(txHashBytes));
                    }

                    return new KeyValuePair<UInt256, ImmutableList<UInt256>>(blockHash, txHashes.ToImmutable());
                });
        }

        public bool TryReadValue(UInt256 blockHash, out ImmutableList<UInt256> blockTxHashes)
        {
            byte[] txHashesBytes;
            if (this.Data.TryGetValue(blockHash.ToByteArray(), out txHashesBytes))
            {
                var txHashes = ImmutableList.CreateBuilder<UInt256>();
                var txHashBytes = new byte[32];
                for (var i = 0; i < txHashesBytes.Length; i += 32)
                {
                    Buffer.BlockCopy(txHashesBytes, i, txHashBytes, 0, 32);
                    txHashes.Add(new UInt256(txHashBytes));
                }

                blockTxHashes = txHashes.ToImmutable();
                return true;
            }
            else
            {
                blockTxHashes = default(ImmutableList<UInt256>);
                return false;
            }
        }

        public bool TryWriteValues(IEnumerable<KeyValuePair<UInt256, WriteValue<ImmutableList<UInt256>>>> keyPairs)
        {
            foreach (var keyPair in keyPairs)
            {
                var txHashesBytes = new byte[keyPair.Value.Value.Count * 32];
                for (var i = 0; i < keyPair.Value.Value.Count; i++)
                {
                    Buffer.BlockCopy(keyPair.Value.Value[i].ToByteArray(), 0, txHashesBytes, i * 32, 32);
                }

                this.Data[keyPair.Key.ToByteArray()] = txHashesBytes;
            }

            return true;
        }

        public void Truncate()
        {
            this.Data.Clear();
        }
    }
}