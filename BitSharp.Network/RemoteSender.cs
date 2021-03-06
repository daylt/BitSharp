﻿using BitSharp.Common;
using BitSharp.Common.ExtensionMethods;
using BitSharp.Core;
using BitSharp.Core.Domain;
using BitSharp.Core.ExtensionMethods;
using BitSharp.Network.Domain;
using NLog;
using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace BitSharp.Network
{
    public class RemoteSender : IDisposable
    {
        public event Action<Peer, Exception> OnFailed;

        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        private readonly SemaphoreSlim semaphore = new SemaphoreSlim(1);
        private readonly Peer owner;
        private readonly Socket socket;

        private bool isDisposed;

        public RemoteSender(Peer owner, Socket socket)
        {
            this.owner = owner;
            this.socket = socket;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!isDisposed && disposing)
            {
                semaphore.Dispose();

                isDisposed = true;
            }
        }

        private void Fail(Exception ex)
        {
            OnFailed?.Invoke(owner, ex);
        }

        public async Task RequestKnownAddressesAsync()
        {
            await SendMessageAsync("getaddr");
        }

        public async Task PingAsync()
        {
            await SendMessageAsync("ping");
        }

        public async Task SendBlock(Block block)
        {
            await Task.Yield();

            var sendBlockMessage = Messaging.ConstructMessage("block", DataEncoder.EncodeBlock(block));

            await SendMessageAsync(sendBlockMessage);
        }

        public async Task SendGetData(InventoryVector invVector)
        {
            await SendGetData(ImmutableArray.Create(invVector));
        }

        public async Task SendGetData(ImmutableArray<InventoryVector> invVectors)
        {
            await Task.Yield();

            var getDataPayload = Messaging.ConstructInventoryPayload(invVectors);
            var getDataMessage = Messaging.ConstructMessage("getdata", NetworkEncoder.EncodeInventoryPayload(getDataPayload));

            await SendMessageAsync(getDataMessage);
        }

        public async Task SendGetHeaders(ImmutableArray<UInt256> blockLocatorHashes, UInt256 hashStop)
        {
            await Task.Yield();

            var getHeadersPayload = Messaging.ConstructGetBlocksPayload(blockLocatorHashes, hashStop);
            var getBlocksMessage = Messaging.ConstructMessage("getheaders", NetworkEncoder.EncodeGetBlocksPayload(getHeadersPayload));

            await SendMessageAsync(getBlocksMessage);
        }

        public async Task SendGetBlocks(ImmutableArray<UInt256> blockLocatorHashes, UInt256 hashStop)
        {
            await Task.Yield();

            var getBlocksPayload = Messaging.ConstructGetBlocksPayload(blockLocatorHashes, hashStop);
            var getBlocksMessage = Messaging.ConstructMessage("getblocks", NetworkEncoder.EncodeGetBlocksPayload(getBlocksPayload));

            await SendMessageAsync(getBlocksMessage);
        }

        public async Task SendHeaders(ImmutableArray<BlockHeader> blockHeaders)
        {
            await Task.Yield();

            using (var payloadStream = new MemoryStream())
            using (var payloadWriter = new BinaryWriter(payloadStream))
            {
                payloadWriter.WriteVarInt((UInt64)blockHeaders.Length);
                foreach (var blockHeader in blockHeaders)
                {
                    DataEncoder.EncodeBlockHeader(payloadWriter, blockHeader);
                    payloadWriter.WriteVarInt(0);
                }

                await SendMessageAsync(Messaging.ConstructMessage("headers", payloadStream.ToArray()));
            }
        }

        public async Task SendInventory(ImmutableArray<InventoryVector> invVectors)
        {
            await Task.Yield();

            var invPayload = Messaging.ConstructInventoryPayload(invVectors);
            var invMessage = Messaging.ConstructMessage("inv", NetworkEncoder.EncodeInventoryPayload(invPayload));

            await SendMessageAsync(invMessage);
        }

        public async Task SendTransaction(EncodedTx transaction)
        {
            await Task.Yield();

            var sendTxMessage = Messaging.ConstructMessage("tx", transaction.TxBytes.ToArray());

            await SendMessageAsync(sendTxMessage);
        }

        public async Task SendVersion(IPEndPoint localEndPoint, IPEndPoint remoteEndPoint, UInt64 nodeId, UInt32 startBlockHeight)
        {
            await Task.Yield();

            var versionPayload = Messaging.ConstructVersionPayload(localEndPoint, remoteEndPoint, nodeId, startBlockHeight);
            var versionMessage = Messaging.ConstructMessage("version", NetworkEncoder.EncodeVersionPayload(versionPayload, withRelay: false));

            await SendMessageAsync(versionMessage);
        }

        public async Task SendVersionAcknowledge()
        {
            await SendMessageAsync("verack");
        }

        public async Task SendMessageAsync(string command)
        {
            await SendMessageAsync(Messaging.ConstructMessage(command, payload: new byte[0]));
        }

        public async Task SendMessageAsync(Message message)
        {
            try
            {
                await semaphore.DoAsync(async () =>
                {
                    using (var stream = new NetworkStream(socket))
                    {
                        var stopwatch = Stopwatch.StartNew();

                        using (var byteStream = new MemoryStream())
                        using (var writer = new BinaryWriter(byteStream))
                        {
                            NetworkEncoder.EncodeMessage(writer, message);

                            var messageBytes = byteStream.ToArray();
                            await stream.WriteAsync(messageBytes, 0, messageBytes.Length);
                        }

                        stopwatch.Stop();

                        if (logger.IsTraceEnabled)
                            logger.Trace($"Sent {message.Command} in {stopwatch.ElapsedMilliseconds} ms\nPayload: {message.Payload.ToArray().ToHexDataString()}");
                    }
                });
            }
            catch (Exception e)
            {
                Fail(e);
            }
        }
    }
}
