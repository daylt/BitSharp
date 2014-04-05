﻿using BitSharp.Blockchain;
using BitSharp.Common;
using BitSharp.Common.ExtensionMethods;
using BitSharp.Data;
using BitSharp.Storage;
using BitSharp.Transactions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ninject;
using NLog;
using Org.BouncyCastle.Crypto.Parameters;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BitSharp.Daemon.Test
{
    public class TestDaemon : IDisposable
    {
        private const UInt64 SATOSHI_PER_BTC = 100 * 1000 * 1000;

        private readonly Random random;
        private readonly IKernel kernel;
        private readonly Logger logger;
        private readonly TransactionManager txManager;
        private readonly ECPrivateKeyParameters coinbasePrivateKey;
        private readonly ECPublicKeyParameters coinbasePublicKey;
        private readonly Miner miner;
        private readonly Block genesisBlock;
        private readonly BlockView blockView;
        private readonly UnitTestRules rules;
        private readonly BlockchainDaemon blockchainDaemon;

        public TestDaemon(Block genesisBlock = null)
        {
            this.random = new Random();

            // initialize kernel
            this.kernel = new StandardKernel();

            // add logging module
            this.kernel.Load(new ConsoleLoggingModule());

            // create the key pair that block rewards will be sent to
            this.txManager = this.kernel.Get<TransactionManager>();
            var keyPair = this.txManager.CreateKeyPair();
            this.coinbasePrivateKey = keyPair.Item1;
            this.coinbasePublicKey = keyPair.Item2;

            // initialize miner
            this.miner = this.kernel.Get<Miner>();

            // create and mine the genesis block
            this.genesisBlock = genesisBlock ?? MineEmptyBlock(0);

            // log startup
            this.logger = kernel.Get<Logger>();
            this.logger.Info("Starting up: {0}".Format2(DateTime.Now));

            // add storage module
            this.kernel.Load(new MemoryStorageModule());

            // add cache module
            this.kernel.Load(new CacheModule());

            // initialize block view
            this.blockView = this.kernel.Get<BlockView>();

            // initialize unit test rules
            this.rules = this.kernel.Get<UnitTestRules>();
            this.rules.SetGenesisBlock(this.genesisBlock);
            this.kernel.Bind<RulesEnum>().ToConstant(RulesEnum.TestNet2);
            this.kernel.Bind<IBlockchainRules>().ToConstant(rules);

            // initialize the blockchain daemon
            this.kernel.Bind<BlockchainDaemon>().ToSelf().InSingletonScope();
            this.blockchainDaemon = this.kernel.Get<BlockchainDaemon>();

            // start the blockchain daemon
            this.blockchainDaemon.Start();
            this.blockchainDaemon.MaxBuilderTime = TimeSpan.Zero;

            // wait for initial work
            this.blockchainDaemon.ForceWorkAndWait();

            // verify initial state
            Assert.AreEqual(0, this.blockchainDaemon.TargetBlock.Height);
            Assert.AreEqual(this.genesisBlock.Hash, this.blockchainDaemon.TargetChain.LastBlock.BlockHash);
            Assert.AreEqual(this.genesisBlock.Hash, this.blockchainDaemon.ChainState.LastBlockHash);
        }

        public void Dispose()
        {
            this.kernel.Dispose();
        }

        public IKernel Kernel { get { return this.kernel; } }

        public TransactionManager TxManager { get { return this.txManager; } }

        public ECPrivateKeyParameters CoinbasePrivateKey { get { return this.coinbasePrivateKey; } }

        public ECPublicKeyParameters CoinbasePublicKey { get { return this.coinbasePublicKey; } }

        public Miner Miner { get { return this.miner; } }

        public Block GenesisBlock { get { return this.genesisBlock; } }

        public BlockView BlockView { get { return this.blockView; } }

        public UnitTestRules Rules { get { return this.rules; } }

        public BlockchainDaemon BlockchainDaemon { get { return this.blockchainDaemon; } }

        public Block CreateEmptyBlock(UInt256 previousBlockHash, UInt256? target = null)
        {
            var coinbaseTx = new Transaction
            (
                version: 0,
                inputs: ImmutableArray.Create
                (
                    new TxInput
                    (
                        previousTxOutputKey: new TxOutputKey
                        (
                            txHash: 0,
                            txOutputIndex: 0
                        ),
                        scriptSignature: ImmutableArray.Create(random.NextBytes(100)),
                        sequence: 0
                    )
                ),
                outputs: ImmutableArray.Create
                (
                    new TxOutput
                    (
                        value: 50 * SATOSHI_PER_BTC,
                        scriptPublicKey: ImmutableArray.Create(this.txManager.CreatePublicKeyScript(coinbasePublicKey))
                    )
                ),
                lockTime: 0
            );

            //Debug.WriteLine("Coinbase Tx Created: {0}".Format2(coinbaseTx.Hash.ToHexNumberString()));

            var transactions = ImmutableArray.Create(coinbaseTx);
            var txHashes = ImmutableList.Create(coinbaseTx.Hash);
            var merkleRoot = DataCalculator.CalculateMerkleRoot(txHashes);

            var block = new Block
            (
                header: new BlockHeader
                (
                    version: 0,
                    previousBlock: previousBlockHash,
                    merkleRoot: merkleRoot,
                    time: 0,
                    bits: DataCalculator.TargetToBits(target ?? UnitTestRules.Target0),
                    nonce: 0
                ),
                transactions: transactions
            );

            return block;
        }

        public Block CreateEmptyBlock(Block prevBlock, UInt256? target = null)
        {
            return CreateEmptyBlock(prevBlock.Hash, target);
        }

        public Block MineBlock(Block block)
        {
            var minedHeader = this.miner.MineBlockHeader(block.Header, DataCalculator.BitsToTarget(block.Header.Bits));
            if (minedHeader == null)
                Assert.Fail("No block could be mined for test data header.");

            block = block.With(Header: minedHeader);

            return block;
        }

        public Block MineEmptyBlock(UInt256 previousBlockHash, UInt256? target = null)
        {
            return MineBlock(CreateEmptyBlock(previousBlockHash, target));
        }

        public Block MineEmptyBlock(Block previousBlock, UInt256? target = null)
        {
            return MineEmptyBlock(previousBlock.Hash, target);
        }

        public Block MineAndAddEmptyBlock(UInt256 previousBlockHash, UInt256? target = null)
        {
            var block = MineEmptyBlock(previousBlockHash, target);
            AddBlock(block);
            return block;
        }

        public Block MineAndAddEmptyBlock(Block prevBlock, UInt256? target = null)
        {
            return MineAndAddEmptyBlock(prevBlock.Hash, target);
        }

        public Block MineAndAddBlock(Block block)
        {
            var minedHeader = this.miner.MineBlockHeader(block.Header, DataCalculator.BitsToTarget(block.Header.Bits));
            if (minedHeader == null)
                Assert.Fail("No block could be mined for test data header.");

            var minedBlock = block.With(Header: minedHeader);
            AddBlock(minedBlock);
            return minedBlock;
        }

        public void AddBlock(Block block)
        {
            this.blockView[block.Hash] = block;

            //TODO ugly to loop like this
            for (var i = 0; i < 10; i++)
                this.blockchainDaemon.ForceWorkAndWait();
        }
    }
}