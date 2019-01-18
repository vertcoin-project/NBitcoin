using NBitcoin.Altcoins.Elements;
using NBitcoin.DataEncoders;
using NBitcoin.RPC;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace NBitcoin.Tests
{
	[Trait("Altcoins", "Altcoins")]
	public class AltcoinTests
	{
		[Fact]
		public void NoCrashQuickTest()
		{
			HashSet<string> coins = new HashSet<string>();
			foreach (var network in NBitcoin.Altcoins.AltNetworkSets.GetAll().ToList())
			{
				if (network == Altcoins.AltNetworkSets.Liquid) // No testnet
					continue;
				Assert.True(coins.Add(network.CryptoCode.ToLowerInvariant()));
				Assert.NotEqual(network.Mainnet, network.Regtest);
				Assert.NotEqual(network.Regtest, network.Testnet);
				Assert.Equal(network.Regtest.NetworkSet, network.Testnet.NetworkSet);
				Assert.Equal(network.Mainnet.NetworkSet, network.Testnet.NetworkSet);
				Assert.Equal(network, network.Testnet.NetworkSet);
				Assert.Equal(NetworkType.Mainnet, network.Mainnet.NetworkType);
				Assert.Equal(NetworkType.Testnet, network.Testnet.NetworkType);
				Assert.Equal(NetworkType.Regtest, network.Regtest.NetworkType);
				Assert.Equal(network.CryptoCode, network.CryptoCode.ToUpperInvariant());
				Assert.Equal(network.Mainnet, Network.GetNetwork(network.CryptoCode.ToLowerInvariant() + "-mainnet"));
				Assert.Equal(network.Testnet, Network.GetNetwork(network.CryptoCode.ToLowerInvariant() + "-testnet"));
				Assert.Equal(network.Regtest, Network.GetNetwork(network.CryptoCode.ToLowerInvariant() + "-regtest"));
			}
		}


		[Fact]
		public void CanCalculateTransactionHash()
		{
			using (var builder = NodeBuilderEx.Create())
			{
				var rpc = builder.CreateNode().CreateRPCClient();
				builder.StartAll();
				var blockHash = rpc.Generate(1)[0];
				var block = rpc.GetBlock(blockHash);

				Transaction walletTx = null;
				try
				{
					walletTx = rpc.GetRawTransaction(block.Transactions[0].GetHash(), block.GetHash());
				}
				// Some nodes does not support the blockid
				catch
				{
					walletTx = rpc.GetRawTransaction(block.Transactions[0].GetHash());
				}
				Assert.Equal(walletTx.ToHex(), block.Transactions[0].ToHex());
			}
		}

		[Fact]
		public void HasCorrectGenesisBlock()
		{
			using (var builder = NodeBuilderEx.Create())
			{
				var rpc = builder.CreateNode().CreateRPCClient();
				builder.StartAll();
				var genesis = rpc.GetBlock(0);
				if (builder.Network == Altcoins.Liquid.Instance.Regtest)
				{
					Assert.Contains(genesis.Transactions.SelectMany(t => t.Outputs).OfType<ElementsTxOut>(), o => o.IsPeggedAsset == true && o.ConfidentialValue.Amount != null && o.ConfidentialValue.Amount != Money.Zero);
				}
				var actual = genesis.GetHash();
				var calculatedGenesis = builder.Network.GetGenesis().GetHash();
				Assert.Equal(calculatedGenesis, actual);
				Assert.Equal(rpc.GetBlockHash(0), calculatedGenesis);
			}
		}

		[Fact]
		public void CanParseBlock()
		{
			using (var builder = NodeBuilderEx.Create())
			{
				var node = builder.CreateNode();
				builder.StartAll();
				var rpc = node.CreateRPCClient();
				rpc.Generate(10);
				var hash = rpc.GetBestBlockHash();
				var b = rpc.GetBlock(hash);
				Assert.NotNull(b);
				Assert.Equal(hash, b.GetHash());

				new ConcurrentChain(builder.Network);
			}
		}

		[Fact]
		public void CanSignTransactions()
		{
			using (var builder = NodeBuilderEx.Create())
			{
				var node = builder.CreateNode();
				builder.StartAll();
				node.Generate(builder.Network.Consensus.CoinbaseMaturity + 1);
				var rpc = node.CreateRPCClient();

				var alice = new Key().GetBitcoinSecret(builder.Network);
				var aliceAddress = alice.GetAddress();
				var txid = rpc.SendToAddress(aliceAddress, Money.Coins(1.0m));
				var tx = rpc.GetRawTransaction(txid);
				var coin = tx.Outputs.AsCoins().First(c => c.ScriptPubKey == aliceAddress.ScriptPubKey);

				// Check the hash calculated correctly
				Assert.Equal(txid, tx.GetHash());
				TransactionBuilder txbuilder = builder.Network.CreateTransactionBuilder();
				txbuilder.AddCoins(coin);
				txbuilder.AddKeys(alice);
				txbuilder.Send(new Key().ScriptPubKey, Money.Coins(0.4m));
				txbuilder.SendFees(Money.Coins(0.001m));
				txbuilder.SetChange(aliceAddress);
				var signed = txbuilder.BuildTransaction(false);
				txbuilder.SignTransactionInPlace(signed);
				txbuilder.Verify(signed, out var err);
				Assert.True(txbuilder.Verify(signed));
				rpc.SendRawTransaction(signed);
			}
		}

		[Fact]
		public void CanParseAddress()
		{
			using (var builder = NodeBuilderEx.Create())
			{
				var node = builder.CreateNode();
				builder.StartAll();
				var addr = node.CreateRPCClient().SendCommand(RPC.RPCOperations.getnewaddress).Result.ToString();
				var addr2 = BitcoinAddress.Create(addr, builder.Network).ToString();
				Assert.Equal(addr, addr2);

				var address = (BitcoinAddress)new Key().PubKey.GetAddress(builder.Network);

				// Test normal address
				var isValid = ((JObject)node.CreateRPCClient().SendCommand("validateaddress", address.ToString()).Result)["isvalid"].Value<bool>();
				Assert.True(isValid);

				// Test p2sh
				address = new Key().PubKey.ScriptPubKey.Hash.ScriptPubKey.GetDestinationAddress(builder.Network);
				isValid = ((JObject)node.CreateRPCClient().SendCommand("validateaddress", address.ToString()).Result)["isvalid"].Value<bool>();
				Assert.True(isValid);
			}
		}

		/// <summary>
		/// This test check if we can scan RPC capabilities
		/// </summary>
		[Fact]
		public void DoesRPCCapabilitiesWellAdvertised()
		{
			using (var builder = NodeBuilderEx.Create())
			{
				var node = builder.CreateNode();
				builder.StartAll();
				node.Generate(builder.Network.Consensus.CoinbaseMaturity + 1);
				var rpc = node.CreateRPCClient();
				rpc.ScanRPCCapabilities();
				Assert.NotNull(rpc.Capabilities);

				CheckCapabilities(rpc, "getnetworkinfo", rpc.Capabilities.SupportGetNetworkInfo);
				CheckCapabilities(rpc, "scantxoutset", rpc.Capabilities.SupportScanUTXOSet);
				CheckCapabilities(rpc, "signrawtransactionwithkey", rpc.Capabilities.SupportSignRawTransactionWith);
				CheckCapabilities(rpc, "estimatesmartfee", rpc.Capabilities.SupportEstimateSmartFee);

				try
				{
					var address = rpc.GetNewAddress(new GetNewAddressRequest()
					{
						AddressType = AddressType.Bech32
					});
					// If this fail, rpc support segwit bug you said it does not
					Assert.Equal(rpc.Capabilities.SupportSegwit, address.ScriptPubKey.IsWitness);
					if (rpc.Capabilities.SupportSegwit)
					{
						Assert.True(builder.Network.Consensus.SupportSegwit, "The node RPC support segwit, but Network.Consensus.SupportSegwit is set to false");
						rpc.SendToAddress(address, Money.Coins(1.0m));
					}
					else
					{
						Assert.False(builder.Network.Consensus.SupportSegwit, "The node RPC does not support segwit, but Network.Consensus.SupportSegwit is set to true (This error can be normal if you are using a old node version)");
					}
				}
				catch (RPCException) when (!rpc.Capabilities.SupportSegwit)
				{
				}
			}
		}
		private void CheckCapabilities(Action command, bool supported)
		{
			if (!supported)
			{
				var ex = Assert.Throws<RPCException>(command);
				Assert.True(ex.RPCCode == RPCErrorCode.RPC_METHOD_NOT_FOUND || ex.RPCCode == RPCErrorCode.RPC_METHOD_DEPRECATED);
			}
			else
			{
				try
				{
					command();
				}
				catch (RPCException ex) when (ex.RPCCode != RPCErrorCode.RPC_METHOD_NOT_FOUND && ex.RPCCode != RPCErrorCode.RPC_METHOD_DEPRECATED)
				{
					// Method exists
				}
			}
		}
		private void CheckCapabilities(RPCClient rpc, string command, bool supported)
		{
			CheckCapabilities(() => rpc.SendCommand(command, "random"), supported);
		}

		[Fact]
		public void CanSyncWithPoW()
		{
			using (var builder = NodeBuilderEx.Create())
			{
				var node = builder.CreateNode();
				builder.StartAll();
				node.Generate(100);

				var nodeClient = node.CreateNodeClient();
				nodeClient.VersionHandshake();
				ConcurrentChain chain = new ConcurrentChain(builder.Network);
				nodeClient.SynchronizeChain(chain, new Protocol.SynchronizeChainOptions() { SkipPoWCheck = false });
				Assert.Equal(100, chain.Height);
			}
		}

		[Fact]
		public void CanProduceVertcoinPoW()
		{
			var expectedscryptn = Encoders.Hex.DecodeData("46e916cd312c8b86a2bab3d09e18691db694c3d3e56d5ad022fd51e7d67605e3");
			var expectedlyra2_v1 = Encoders.Hex.DecodeData("abcff45917ad5ee83905d669784e5a89c3aeecd25d303d8758d089b4cb3d9919");
			var expectedlyra2_v2 = Encoders.Hex.DecodeData("d40b0ed002f15dda9d73a775573dadda998330650b80025f7b8099f6e6c10a01");
			var expectedlyra2_v3 = Encoders.Hex.DecodeData("b94ea7b6b063c99729ee669d4bea77914733084b5cbe6df09ab89ae8e10b95da");
			var expectedlyra2re = Encoders.Hex.DecodeData("5e207f8828344cbcd58dcfb55b42d94f109d3c6fa74d9aafb67744a303741464");
			var expectedlyra2rev2 = Encoders.Hex.DecodeData("df3ccd797f3c039d2a586a795829a40ea8532a62c9ae9f7cd74f8f8f506577f5");
			var expectedlyra2rev3 = Encoders.Hex.DecodeData("5d7b298258e78881c7831ba1e46751b089efdf1fdb9eb01edd03b8d7ed39eafb");
			var data = Encoders.Hex.DecodeData("700000005d385ba114d079971b29a9418fd0549e7d68a95c7f168621a314201000000000578586d149fd07b22f3a8a347c516de7052f034d2b76ff68e0d6ecff9b77a45489e3fd511732011df0731000");

			byte[] Lyra(Altcoins.GincoinInternals.Lyra2.Lyra2Version rev)
			{
				var lyra2bytes = new byte[32];
				var size = rev >= Altcoins.GincoinInternals.Lyra2.Lyra2Version.v2 ? 4ul : 8ul;
				new Altcoins.GincoinInternals.Lyra2.Lyra2(rev).Calculate(lyra2bytes, data, data, 1, size, size);
				return lyra2bytes;
			}

			var scryptn = Crypto.SCrypt.ComputeDerivedKey(data, data, 2048, 1, 1, null, 32);
			var lyra2_v1 = Lyra(Altcoins.GincoinInternals.Lyra2.Lyra2Version.v1);
			var lyra2_v2 = Lyra(Altcoins.GincoinInternals.Lyra2.Lyra2Version.v2);
			var lyra2_v3 = Lyra(Altcoins.GincoinInternals.Lyra2.Lyra2Version.v3);
			var lyra2re = Altcoins.GincoinInternals.Lyra2.Lyra2RE.ComputeHash(data);
			var lyra2rev2 = Altcoins.GincoinInternals.Lyra2.Lyra2REv2.ComputeHash(data);
			var lyra2rev3 = Altcoins.GincoinInternals.Lyra2.Lyra2REv3.ComputeHash(data);

			Assert.Equal(expectedscryptn, scryptn);
			Assert.Equal(expectedlyra2_v1, lyra2_v1);
			Assert.Equal(expectedlyra2_v2, lyra2_v2);
			Assert.Equal(expectedlyra2_v3, lyra2_v3);
			Assert.Equal(expectedlyra2re, lyra2re);
			Assert.Equal(expectedlyra2rev2, lyra2rev2);
			Assert.Equal(expectedlyra2rev3, lyra2rev3);
		}

		[Fact]
		public void CorrectCoinMaturity()
		{
			using (var builder = NodeBuilderEx.Create())
			{
				var node = builder.CreateNode();
				builder.StartAll();
				node.Generate(builder.Network.Consensus.CoinbaseMaturity);
				var rpc = node.CreateRPCClient();
				Assert.Equal(Money.Zero, rpc.GetBalance());
				node.Generate(1);
				Assert.NotEqual(Money.Zero, rpc.GetBalance());
			}
		}

		[Fact]
		public void CanSyncWithoutPoW()
		{
			using (var builder = NodeBuilderEx.Create())
			{
				var node = builder.CreateNode();
				builder.StartAll();
				node.Generate(100);
				var nodeClient = node.CreateNodeClient();
				nodeClient.VersionHandshake();
				ConcurrentChain chain = new ConcurrentChain(builder.Network);
				nodeClient.SynchronizeChain(chain, new Protocol.SynchronizeChainOptions() { SkipPoWCheck = true });
				Assert.Equal(node.CreateRPCClient().GetBestBlockHash(), chain.Tip.HashBlock);
				Assert.Equal(100, chain.Height);

				// If it fails, override Block.GetConsensusFactory()
				var b = node.CreateRPCClient().GetBlock(50);
				Assert.Equal(b.WithOptions(TransactionOptions.Witness).Header.GetType(), chain.GetBlock(50).Header.GetType());

				var b2 = nodeClient.GetBlocks(new Protocol.SynchronizeChainOptions() { SkipPoWCheck = true }).ToArray()[50];
				Assert.Equal(b2.Header.GetType(), chain.GetBlock(50).Header.GetType());
			}
		}
	}
}