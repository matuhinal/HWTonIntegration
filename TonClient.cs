namespace HWTonIntegration
{
    public class Ton
    {
        private readonly ITonClient _tonClient;
        private const string RootPublic = "rootPublic";
        private const string RootPrivate = "rootPrivate";
        private const string RootTokenContract = "rootContract";

        private static readonly string App =
            Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);

        private readonly Abi _rootTokenAbi;
        private readonly Abi _tokenWalletAbi;
        private readonly KeyPair _contractKeys;

        public Ton()
        {
            _tonClient = TonClient.Create(new ClientConfig
            {
                Network = new NetworkConfig
                {
                    ServerAddress = "https://main.ton.dev/",
                    MessageRetriesCount = 10,
                    OutOfSyncThreshold = 2500
                }
            }, new TonLogger());

            _rootTokenAbi = new Abi.Contract()
            {
                Value = JsonConvert.DeserializeObject<AbiContract>(GetFile("RootTokenContract.abi"))
            };

            _tokenWalletAbi = new Abi.Contract()
            {
                Value = JsonConvert.DeserializeObject<AbiContract>(GetFile("TONTokenWallet.abi"))
            };

            _contractKeys = new KeyPair
            {
                Public = RootPublic,
                Secret = RootPrivate
            };
        }

        public async Task ContractMint(uint amount)
        {
            var mintMessage = new ParamsOfEncodeMessage
            {
                Address = RootTokenContract,
                Abi = _rootTokenAbi,
                CallSet = new CallSet
                {
                    FunctionName = "mint",
                    Input = new { tokens = amount }.ToJson()
                }
            };
            await _tonClient.Processing.ProcessMessageAsync(
                new ParamsOfProcessMessage
                {
                    MessageEncodeParams = mintMessage
                });
        }

        public async Task<KeyPair> GenerateKeyPair()
        {
            try
            {
                return await _tonClient.Crypto.GenerateRandomSignKeysAsync();
            }
            catch
            {
                return null;
            }
        }

        public async Task<string> GenerateWalletAddress(KeyPair keys)
        {
            var accountBoc = await GetAccountBoc(RootTokenContract);

            var @params = new ParamsOfEncodeMessage
            {
                Abi = _rootTokenAbi,
                Address = RootTokenContract,
                CallSet = new CallSet
                {
                    FunctionName = "getWalletAddress",
                    Input = new
                    {
                        workchain_id = 0,
                        pubkey = $"0x{keys.Public}",
                        owner_std_addr = 0,
                    }.ToJson()
                },
                Signer = new Signer.None()
            };

            var message = await _tonClient.Abi.EncodeMessageAsync(@params);
            try
            {
                var resultOfProcessMessage = await _tonClient.Tvm.RunTvmAsync(new ParamsOfRunTvm
                {
                    Abi = _rootTokenAbi,
                    Account = accountBoc,
                    Message = message.Message
                });
                return resultOfProcessMessage.Decoded.Output["value0"]?.ToString();
            }
            catch
            {
                return null;
            }
        }

        public async Task<string> TotalSupply()
        {
            var accountBoc = await GetAccountBoc(RootTokenContract);

            var @params = new ParamsOfEncodeMessage
            {
                Abi = _rootTokenAbi,
                Address = RootTokenContract,
                CallSet = new CallSet
                {
                    FunctionName = "getTotalSupply"
                },
                Signer = new Signer.None()
            };

            var message = await _tonClient.Abi.EncodeMessageAsync(@params);

            try
            {
                var resultOfProcessMessage = await _tonClient.Tvm.RunTvmAsync(new ParamsOfRunTvm
                {
                    Abi = _rootTokenAbi,
                    Account = accountBoc,
                    Message = message.Message
                });
                return resultOfProcessMessage.Decoded.Output["value0"]?.ToString();
            }
            catch
            {
                return null;
            }

        }

        public async Task<string> Transfer(string to, string dest, uint tokens, string @public, string secret)
        {
            var message = new ParamsOfEncodeMessage
            {
                Address = dest,
                Abi = _tokenWalletAbi,
                CallSet = new CallSet
                {
                    FunctionName = "transfer",
                    Input = new
                    {
                        dest = to,
                        tokens,
                        grams = 25000000
                    }.ToJson()
                },
                Signer = new Signer.Keys
                {
                    KeysProperty = new KeyPair
                    {
                        Public = @public,
                        Secret = secret
                    }
                }
            };

            try
            {
                var result = await _tonClient.Processing.ProcessMessageAsync(
                    new ParamsOfProcessMessage
                    {
                        MessageEncodeParams = message
                    });

                return result.Transaction?["id"]?.ToString();
            }
            catch
            {
                return null;
            }
        }

        public async Task<string> DeployWallet(string address, uint tokens)
        {
            var message = new ParamsOfEncodeMessage
            {
                Address = RootTokenContract,
                Abi = _rootTokenAbi,
                CallSet = new CallSet
                {
                    FunctionName = "deployWallet",
                    Input = new
                    {
                        _answer_id = 0,
                        workchain_id = 0,
                        pubkey = $"0x{address}",
                        internal_owner = 0,
                        grams = 25000000,
                        tokens
                    }.ToJson()
                },
                Signer = new Signer.Keys
                {
                    KeysProperty = _contractKeys
                }
            };

            try
            {
                var result = await _tonClient.Processing.ProcessMessageAsync(
                    new ParamsOfProcessMessage
                    {
                        MessageEncodeParams = message
                    });

                return result.Decoded.Output["value0"]?.ToString();
            }
            catch (Exception e)
            {
                return null;
            }
        }

        public async Task<bool> Grant(string address, uint tokens, long grams)
        {
            var message = new ParamsOfEncodeMessage
            {
                Address = RootTokenContract,
                Abi = _rootTokenAbi,
                CallSet = new CallSet
                {
                    FunctionName = "grant",
                    Input = new
                    {
                        dest = address,
                        tokens,
                        grams
                    }.ToJson()
                },
                Signer = new Signer.Keys
                {
                    KeysProperty = _contractKeys
                }
            };

            try
            {
                await _tonClient.Processing.ProcessMessageAsync(
                    new ParamsOfProcessMessage
                    {
                        MessageEncodeParams = message
                    });

                return true;
            }
            catch
            {
                return false;
            }
        }

        public async Task<uint> GetWalletBalance(string address)
        {
            var accountBoc = await GetAccountBoc(address);

            var @params = new ParamsOfEncodeMessage
            {
                Abi = _tokenWalletAbi,
                Address = address,
                CallSet = new CallSet
                {
                    FunctionName = "getBalance"
                },
                Signer = new Signer.None()
            };

            var message = await _tonClient.Abi.EncodeMessageAsync(@params);

            try
            {
                var resultOfProcessMessage = await _tonClient.Tvm.RunTvmAsync(new ParamsOfRunTvm
                {
                    Abi = _rootTokenAbi,
                    Account = accountBoc,
                    Message = message.Message
                });
                return uint.Parse(resultOfProcessMessage.Decoded.Output["value0"]?.ToString()!);
            }
            catch
            {
                return 0;
            }
        }

        private async Task<string> GetAccountBoc(string address)
        {
            var accountBocResult = await _tonClient.Net.QueryCollectionAsync(new ParamsOfQueryCollection
            {
                Collection = "accounts",
                Filter = new { id = new { eq = address } }.ToJson(),
                Result = "boc",
                Limit = 1
            });

            var accountBoc = accountBocResult.Result[0]["boc"]?.ToString();
            return accountBoc;
        }

        public async Task<ulong> CheckGrammBalance(string address)
        {
            var result = await _tonClient.Net.QueryCollectionAsync(new ParamsOfQueryCollection
            {
                Collection = "accounts",
                Filter = new { id = new { eq = address } }.ToJson(),
                Result = "balance",
                Limit = 1
            });

            if (result.Result.Length != 0)
                return HexToDec(result.Result[0]["balance"]!.Value<string>());

            return 0;
        }


        public async Task CheckContractBalance()
        {
            var result = await _tonClient.Net.QueryCollectionAsync(new ParamsOfQueryCollection
            {
                Collection = "accounts",
                Filter = new { id = new { eq = RootTokenContract } }.ToJson(),
                Result = "balance",
                Limit = 1
            });

            if (result.Result.Length != 0)
                Console.WriteLine(HexToDec(result.Result[0]["balance"]!.Value<string>()));
        }

        public static string GetFile(string name)
        {
            using var str = new StreamReader($"{App}/Contracts/{name}");
            var template = str.ReadToEnd();

            return template;
        }

        public static ulong HexToDec(string hexValue)
        {
            return Convert.ToUInt64(hexValue, 16);
        }
    }
}
