﻿using BitSharp.Common;
using BitSharp.Common.ExtensionMethods;
using BitSharp.Daemon;
using BitSharp.Data;
using BitSharp.Network;
using BitSharp.Storage;
using NLog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BitSharp.Node
{
    public class HeadersRequestWorker : Worker
    {
        private static readonly TimeSpan STALE_REQUEST_TIME = TimeSpan.FromSeconds(60);

        private readonly Logger logger;
        private readonly LocalClient localClient;
        private readonly BlockchainDaemon blockchainDaemon;
        private readonly BlockHeaderCache blockHeaderCache;

        private readonly ConcurrentDictionary<IPEndPoint, DateTime> headersRequestsByPeer;

        private readonly WorkerMethod flushWorker;
        private readonly ConcurrentQueue<Tuple<RemoteNode, BlockHeader>> flushQueue;

        public HeadersRequestWorker(Logger logger, WorkerConfig workerConfig, LocalClient localClient, BlockchainDaemon blockchainDaemon, BlockHeaderCache blockHeaderCache)
            : base("HeadersRequestWorker", workerConfig.initialNotify, workerConfig.minIdleTime, workerConfig.maxIdleTime, logger)
        {
            this.logger = logger;
            this.localClient = localClient;
            this.blockchainDaemon = blockchainDaemon;
            this.blockHeaderCache = blockHeaderCache;

            this.headersRequestsByPeer = new ConcurrentDictionary<IPEndPoint, DateTime>();

            this.localClient.OnBlockHeader += HandleBlockHeader;
            this.blockchainDaemon.OnTargetChainChanged += HandleTargetChainChanged;

            this.flushWorker = new WorkerMethod("HeadersRequestWorker.FlushWorker", FlushWorkerMethod, initialNotify: true, minIdleTime: TimeSpan.Zero, maxIdleTime: TimeSpan.MaxValue, logger: this.logger);
            this.flushQueue = new ConcurrentQueue<Tuple<RemoteNode, BlockHeader>>();
        }

        protected override void SubDispose()
        {
            this.localClient.OnBlockHeader -= HandleBlockHeader;
            this.blockchainDaemon.OnTargetChainChanged -= HandleTargetChainChanged;

            this.flushWorker.Dispose();
        }

        protected override void SubStart()
        {
            this.flushWorker.Start();
        }

        protected override void SubStop()
        {
            this.flushWorker.Stop();
        }

        protected override void WorkAction()
        {
            var now = DateTime.UtcNow;
            var requestTasks = new List<Task>();

            var peerCount = this.localClient.ConnectedPeers.Count;
            if (peerCount == 0)
                return;

            var targetChainLocal = this.blockchainDaemon.TargetChain;
            if (targetChainLocal == null)
                return;

            var blockLocatorHashes = CalculateBlockLocatorHashes(targetChainLocal.Blocks);

            // remove any stale requests from the peer's list of requests
            this.headersRequestsByPeer.RemoveWhere(x => (now - x.Value) > STALE_REQUEST_TIME);

            // loop through each connected peer
            foreach (var peer in this.localClient.ConnectedPeers)
            {
                // determine if a new request can be made
                if (this.headersRequestsByPeer.TryAdd(peer.Key, now))
                {
                    // send out the request for headers
                    requestTasks.Add(peer.Value.Sender.SendGetHeaders(blockLocatorHashes, hashStop: 0));
                }
            }
        }

        private void FlushWorkerMethod()
        {
            Tuple<RemoteNode, BlockHeader> tuple;
            while (this.flushQueue.TryDequeue(out tuple))
            {
                var remoteNode = tuple.Item1;
                var blockHeader = tuple.Item2;

                this.blockHeaderCache.TryAdd(blockHeader.Hash, blockHeader);

                DateTime ignore;
                this.headersRequestsByPeer.TryRemove(remoteNode.RemoteEndPoint, out ignore);

                this.NotifyWork();
            }
        }

        private void HandleBlockHeader(RemoteNode remoteNode, BlockHeader blockHeader)
        {
            this.flushQueue.Enqueue(Tuple.Create(remoteNode, blockHeader));
            this.flushWorker.NotifyWork();
        }

        private void HandleTargetChainChanged(object sender, EventArgs e)
        {
            this.NotifyWork();
        }

        private static ImmutableArray<UInt256> CalculateBlockLocatorHashes(IImmutableList<ChainedBlock> blockHashes)
        {
            var blockLocatorHashes = new List<UInt256>();

            if (blockHashes.Count > 0)
            {
                var step = 1;
                var start = 0;
                for (var i = blockHashes.Count - 1; i > 0; i -= step, start++)
                {
                    if (start >= 10)
                        step *= 2;

                    blockLocatorHashes.Add(blockHashes[i].BlockHash);
                }
                blockLocatorHashes.Add(blockHashes[0].BlockHash);
            }

            return blockLocatorHashes.ToImmutableArray();
        }
    }
}
