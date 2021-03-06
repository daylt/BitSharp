﻿using BitSharp.Common;
using BitSharp.Common.ExtensionMethods;
using BitSharp.Core.Domain;
using BitSharp.Core.Storage;
using NLog;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;

namespace BitSharp.Core.Builders
{
    internal class UnconfirmedTxesBuilder : IDisposable
    {
        public event EventHandler<UnconfirmedTxAddedEventArgs> UnconfirmedTxAdded;
        public event EventHandler<TxesConfirmedEventArgs> TxesConfirmed;
        public event EventHandler<TxesUnconfirmedEventArgs> TxesUnconfirmed;

        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        private readonly ICoreDaemon coreDaemon;
        private readonly ICoreStorage coreStorage;
        private readonly IStorageManager storageManager;

        private readonly ReaderWriterLockSlim updateLock = new ReaderWriterLockSlim();
        private readonly ReaderWriterLockSlim commitLock = new ReaderWriterLockSlim();

        private Lazy<Chain> chain;

        private bool disposed;

        public UnconfirmedTxesBuilder(ICoreDaemon coreDaemon, ICoreStorage coreStorage, IStorageManager storageManager)
        {
            this.coreDaemon = coreDaemon;
            this.coreStorage = coreStorage;
            this.storageManager = storageManager;

            this.chain = new Lazy<Chain>(() => LoadChain());
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposed && disposing)
            {
                updateLock.Dispose();
                commitLock.Dispose();

                disposed = true;
            }
        }

        public Chain Chain => chain.Value;

        public bool ContainsTransaction(UInt256 txHash)
        {
            using (var handle = storageManager.OpenUnconfirmedTxesCursor())
            {
                var unconfirmedTxesCursor = handle.Item;
                unconfirmedTxesCursor.BeginTransaction(readOnly: true);

                return unconfirmedTxesCursor.ContainsTransaction(txHash);
            }
        }

        public bool TryAddTransaction(DecodedTx decodedTx)
        {
            if (ContainsTransaction(decodedTx.Hash))
                // unconfirmed tx already exists
                return false;

            var tx = decodedTx.Transaction;
            UnconfirmedTx unconfirmedTx;

            // allow concurrent transaction adds if underlying storage supports it
            // in either case, lock waits for block add/rollback to finish
            if (storageManager.IsUnconfirmedTxesConcurrent)
                updateLock.EnterReadLock();
            else
                updateLock.EnterWriteLock();
            try
            {
                using (var chainState = coreDaemon.GetChainState())
                {
                    // verify each input is available to spend
                    var prevTxOutputKeys = new HashSet<TxOutputKey>();
                    var prevTxOutputs = ImmutableArray.CreateBuilder<PrevTxOutput>(tx.Inputs.Length);
                    var inputValue = 0UL;
                    for (var inputIndex = 0; inputIndex < tx.Inputs.Length; inputIndex++)
                    {
                        var input = tx.Inputs[inputIndex];

                        if (!prevTxOutputKeys.Add(input.PrevTxOutputKey))
                            // tx double spends one of its own inputs
                            return false;

                        UnspentTx unspentTx;
                        if (!chainState.TryGetUnspentTx(input.PrevTxHash, out unspentTx))
                            // input's prev output does not exist
                            return false;

                        if (input.PrevTxOutputIndex >= unspentTx.OutputStates.Length)
                            // input's prev output does not exist
                            return false;

                        if (unspentTx.OutputStates[(int)input.PrevTxOutputIndex] != OutputState.Unspent)
                            // input's prev output has already been spent
                            return false;

                        TxOutput txOutput;
                        if (!chainState.TryGetUnspentTxOutput(input.PrevTxOutputKey, out txOutput))
                            // input's prev output does not exist
                            return false;

                        var prevTxOutput = new PrevTxOutput(txOutput, unspentTx);
                        prevTxOutputs.Add(prevTxOutput);
                        checked { inputValue += prevTxOutput.Value; }
                    }

                    var outputValue = 0UL;
                    for (var outputIndex = 0; outputIndex < tx.Outputs.Length; outputIndex++)
                    {
                        var output = tx.Outputs[outputIndex];
                        checked { outputValue += output.Value; }
                    }

                    if (outputValue > inputValue)
                        // transaction spends more than its inputs
                        return false;

                    // validation passed

                    // create the unconfirmed tx
                    var blockTx = new DecodedBlockTx(-1, decodedTx);
                    var validatableTx = new ValidatableTx(blockTx, null, prevTxOutputs.ToImmutable());
                    unconfirmedTx = new UnconfirmedTx(validatableTx, DateTimeOffset.Now);

                    // add the unconfirmed tx
                    using (var handle = storageManager.OpenUnconfirmedTxesCursor())
                    {
                        var unconfirmedTxesCursor = handle.Item;

                        unconfirmedTxesCursor.BeginTransaction();
                        if (unconfirmedTxesCursor.TryAddTransaction(unconfirmedTx))
                        {
                            unconfirmedTxesCursor.CommitTransaction();
                        }
                        else
                            // unconfirmed tx already exists
                            return false;
                    }
                }
            }
            finally
            {
                if (storageManager.IsUnconfirmedTxesConcurrent)
                    updateLock.ExitReadLock();
                else
                    updateLock.ExitWriteLock();
            }

            UnconfirmedTxAdded?.Invoke(this, new UnconfirmedTxAddedEventArgs(unconfirmedTx));
            return true;
        }

        public bool TryGetTransaction(UInt256 txHash, out UnconfirmedTx unconfirmedTx)
        {
            using (var handle = storageManager.OpenUnconfirmedTxesCursor())
            {
                var unconfirmedTxesCursor = handle.Item;
                unconfirmedTxesCursor.BeginTransaction(readOnly: true);

                return unconfirmedTxesCursor.TryGetTransaction(txHash, out unconfirmedTx);
            }
        }

        public ImmutableDictionary<UInt256, UnconfirmedTx> GetTransactionsSpending(UInt256 txHash, uint outputIndex)
        {
            return GetTransactionsSpending(new TxOutputKey(txHash, outputIndex));
        }

        public ImmutableDictionary<UInt256, UnconfirmedTx> GetTransactionsSpending(TxOutputKey txOutputKey)
        {
            using (var handle = storageManager.OpenUnconfirmedTxesCursor())
            {
                var unconfirmedTxesCursor = handle.Item;
                unconfirmedTxesCursor.BeginTransaction(readOnly: true);

                return unconfirmedTxesCursor.GetTransactionsSpending(txOutputKey);
            }
        }

        public void AddBlock(ChainedHeader chainedHeader, IEnumerable<BlockTx> blockTxes, CancellationToken cancelToken = default(CancellationToken))
        {
            var confirmedTxes = ImmutableDictionary.CreateBuilder<UInt256, UnconfirmedTx>();

            updateLock.DoWrite(() =>
            {
                using (var handle = storageManager.OpenUnconfirmedTxesCursor())
                {
                    var unconfirmedTxesCursor = handle.Item;

                    unconfirmedTxesCursor.BeginTransaction();

                    var newChain = chain.Value.ToBuilder().AddBlock(chainedHeader).ToImmutable();

                    foreach (var blockTx in blockTxes)
                    {
                        // remove any txes confirmed in the block from the list of unconfirmed txes
                        UnconfirmedTx unconfirmedTx;
                        if (unconfirmedTxesCursor.TryGetTransaction(blockTx.Hash, out unconfirmedTx))
                        {
                            // track confirmed txes
                            confirmedTxes.Add(unconfirmedTx.Hash, unconfirmedTx);

                            if (!unconfirmedTxesCursor.TryRemoveTransaction(blockTx.Hash))
                                throw new InvalidOperationException();
                        }

                        // check for and remove any unconfirmed txes that conflict with the confirmed tx
                        var confirmedTx = blockTx.EncodedTx.Decode().Transaction;
                        foreach (var input in confirmedTx.Inputs)
                        {
                            var conflictingTxes = unconfirmedTxesCursor.GetTransactionsSpending(input.PrevTxOutputKey);
                            if (conflictingTxes.Count > 0)
                            {
                                logger.Warn($"Removing {conflictingTxes.Count} conflicting txes from the unconfirmed transaction pool");

                                // remove the conflicting unconfirmed txes
                                foreach (var conflictingTx in conflictingTxes.Keys)
                                    if (!unconfirmedTxesCursor.TryRemoveTransaction(conflictingTx))
                                        throw new StorageCorruptException(StorageType.UnconfirmedTxes, $"{conflictingTx} is indexed but not present");
                            }
                        }
                    }

                    unconfirmedTxesCursor.ChainTip = chainedHeader;

                    commitLock.DoWrite(() =>
                    {
                        unconfirmedTxesCursor.CommitTransaction();
                        chain = new Lazy<Chain>(() => newChain).Force();
                    });
                }
            });

            TxesConfirmed?.Invoke(this, new TxesConfirmedEventArgs(chainedHeader, confirmedTxes.ToImmutable()));
        }

        public void RollbackBlock(ChainedHeader chainedHeader, IEnumerable<BlockTx> blockTxes)
        {
            var unconfirmedTxes = ImmutableDictionary.CreateBuilder<UInt256, BlockTx>();

            updateLock.DoWrite(() =>
            {
                using (var handle = storageManager.OpenUnconfirmedTxesCursor())
                {
                    var unconfirmedTxesCursor = handle.Item;

                    unconfirmedTxesCursor.BeginTransaction();

                    var newChain = chain.Value.ToBuilder().RemoveBlock(chainedHeader).ToImmutable();

                    foreach (var blockTx in blockTxes)
                    {
                        unconfirmedTxes.Add(blockTx.Hash, blockTx);
                    }

                    unconfirmedTxesCursor.ChainTip = chainedHeader;

                    commitLock.DoWrite(() =>
                    {
                        unconfirmedTxesCursor.CommitTransaction();
                        chain = new Lazy<Chain>(() => newChain).Force();
                    });
                }
            });

            TxesUnconfirmed?.Invoke(this, new TxesUnconfirmedEventArgs(chainedHeader, unconfirmedTxes.ToImmutable()));
        }

        public UnconfirmedTxes ToImmutable()
        {
            return commitLock.DoRead(() =>
                new UnconfirmedTxes(chain.Value, storageManager));
        }

        private Chain LoadChain()
        {
            using (var handle = storageManager.OpenUnconfirmedTxesCursor())
            {
                var unconfirmedTxesCursor = handle.Item;

                unconfirmedTxesCursor.BeginTransaction(readOnly: true);

                var chainTip = unconfirmedTxesCursor.ChainTip;
                var chainTipHash = chainTip?.Hash;

                Chain chain;
                if (!coreStorage.TryReadChain(chainTipHash, out chain))
                    throw new StorageCorruptException(StorageType.UnconfirmedTxes, "UnconfirmedTxes is missing header.");

                return chain;
            }
        }
    }
}
