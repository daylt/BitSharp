﻿using System;
using System.Collections.Immutable;
using BitSharp.Common;
using BitSharp.Core.Domain;

namespace BitSharp.Core.Storage
{
    public interface IUnconfirmedTxesCursor : IDisposable
    {
        bool InTransaction { get; }

        void BeginTransaction(bool readOnly = false);

        void CommitTransaction();

        void RollbackTransaction();

        ChainedHeader ChainTip { get; set; }

        int UnconfirmedTxCount { get; set; }

        bool ContainsTransaction(UInt256 txHash);

        bool TryGetTransaction(UInt256 txHash, out UnconfirmedTx unconfimedTx);

        bool TryAddTransaction(UnconfirmedTx unconfirmedTx);

        bool TryRemoveTransaction(UInt256 txHash);

        ImmutableDictionary<UInt256, UnconfirmedTx> GetTransactionsSpending(TxOutputKey prevTxOutputKey);
    }
}
