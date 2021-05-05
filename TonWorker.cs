namespace HWTonIntegration
{
    [DisallowConcurrentExecution]
    public class TonJob : IJob
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger _logger;
        private readonly Ton _ton;

        public const string CharityWallet = "wallet";
        public const string CharityPublic = "public";
        public const string CharitySecret = "secret";

        public const string StoreWallet = "wallet";
        public const string StorePublic = "public";       
        public const string StoreSecret = "secret";      
        
        public TonJob(ApplicationDbContext context, ILogger<TonJob> logger)
        {
            _context = context;
            _logger = logger;
            _ton = new Ton();
        }


        public async Task Execute(IJobExecutionContext context)
        {
            var donors = await GetDonors();

            await CreateRecipientsWallets();

            await CreateDonorsWallets();

            await GrantDonations(donors.Where(i => i.Wallet != null));

            await DonationTransactions(donors.Where(i => i.Wallet != null));

            await DistributedDonations();

            await SpentTransactions();

            await ExpireTransactions();
        }

        private async Task ExpireTransactions()
        {
            var recipients = await GetRecipients();
            var transactions = await _context.Transactions.Where(i => i.Hash == null && i.Type == 6 && i.Amount > 0).ToListAsync();

            foreach (var tx in transactions)
            {
                var recipient = recipients.FirstOrDefault(i => i.Id == tx.From);
                if (recipient?.Wallet != null)
                {
                    var wallet = await _context.Wallets.FirstOrDefaultAsync(i => i.Address == recipient.Wallet);
                    if (wallet != null)
                    {
                        var balance = await _ton.CheckGrammBalance(recipient.Wallet);
                        if (balance < 50000000)
                        {
                            await _ton.Grant(recipient.Wallet, 0, 50000000);
                        }

                        var hash = await _ton.Transfer(CharityWallet, recipient.Wallet, Convert.ToUInt32(tx.Amount * 100), wallet.Public,
                            wallet.Secret);

                        tx.Hash = hash;

                        _context.Entry(tx).State = EntityState.Modified;
                        await _context.SaveChangesAsync();
                    }
                }
            }
        }

        private async Task SpentTransactions()
        {
            var recipients = await GetRecipients();
            var transactions = await _context.Transactions.Where(i => i.Hash == null && i.Type == 5 && i.Amount > 0).ToListAsync();

            foreach (var tx in transactions)
            {
                var recipient = recipients.FirstOrDefault(i => i.Id == tx.From);
                if (recipient?.Wallet != null)
                {
                    var wallet = await _context.Wallets.FirstOrDefaultAsync(i => i.Address == recipient.Wallet);
                    if (wallet != null)
                    {
                        var balance = await _ton.CheckGrammBalance(recipient.Wallet);
                        if (balance < 50000000)
                        {
                            await _ton.Grant(recipient.Wallet, 0, 50000000);
                        }

                        var hash = await _ton.Transfer(StoreWallet, recipient.Wallet, Convert.ToUInt32(tx.Amount * 100), wallet.Public,
                            wallet.Secret);

                        tx.Hash = hash;

                        _context.Entry(tx).State = EntityState.Modified;
                        await _context.SaveChangesAsync();
                    }
                }
            }
        }

        private async Task DistributedDonations()
        {
            var recipients = await GetRecipients();

            var transactions = await _context.Transactions.Where(i => i.Hash == null && i.Type == 4 && i.Amount > 0).ToListAsync();

            foreach (var tx in transactions)
            {
                var recipient = recipients.FirstOrDefault(i => i.Id == tx.To);
                if (recipient?.Wallet != null)
                {
                    var wallet = await _context.Wallets.FirstOrDefaultAsync(i => i.Address == recipient.Wallet);
                    if (wallet != null)
                    {
                        var balanceCharity = await _ton.CheckGrammBalance(CharityWallet);
                        if (balanceCharity < 50000000)
                        {
                            await _ton.Grant(CharityWallet, 0, 5000000000);
                        }

                        var balanceRecipient = await _ton.CheckGrammBalance(recipient.Wallet);
                        if (balanceRecipient < 1000000)
                        {
                            await _ton.Grant(CharityWallet, 0, 1000000);
                        }

                        var hash = await _ton.Transfer(recipient.Wallet, CharityWallet, Convert.ToUInt32(tx.Amount * 100), CharityPublic,
                            CharitySecret);

                        tx.Hash = hash;

                        _context.Entry(tx).State = EntityState.Modified;
                        await _context.SaveChangesAsync();
                    }
                }
            }
        }

        private async Task DonationTransactions(IEnumerable<CartJob.Donor> donors)
        {
            var transactions = await _context.Transactions.Where(i => i.Hash == null && (i.Type == 1 || i.Type == 3) && i.Amount > 0).ToListAsync();

            foreach (var tx in transactions)
            {
                var donor = donors.FirstOrDefault(i => i.Id == tx.From);
                if (donor?.Wallet != null)
                {
                    var wallet = await _context.Wallets.FirstOrDefaultAsync(i => i.Address == donor.Wallet);
                    if (wallet != null)
                    {
                        var balance = await _ton.CheckGrammBalance(donor.Wallet);
                        if (balance < 50000000)
                        {
                            await _ton.Grant(donor.Wallet, 0, 50000000);
                        }

                        var tokens = await _ton.GetWalletBalance(donor.Wallet);

                        if (tokens >= Convert.ToUInt32(tx.Amount * 100))
                        {
                            var hash = await _ton.Transfer(CharityWallet, donor.Wallet,
                                Convert.ToUInt32(tx.Amount * 100), wallet.Public,
                                wallet.Secret);

                            tx.Hash = hash;

                            _context.Entry(tx).State = EntityState.Modified;
                            await _context.SaveChangesAsync();
                        }
                        else
                        {
                            await _ton.Grant(donor.Wallet, Convert.ToUInt32(tx.Amount * 100) - tokens, 50000000);
                        }
                    }
                }
            }
        }
        
        private async Task CreateDonorsWallets()
        {
            var donors = await GetDonorsWithoutWallet();

            foreach (var donor in donors)
            {
                var keys = await _ton.GenerateKeyPair();

                if (keys == null) continue;

                var wallet = await _ton.DeployWallet(keys.Public, 0);

                if (wallet == null) continue;

                if (!await SaveDonorWallet(wallet, donor.Id)) continue;

                await _context.Wallets.AddAsync(new Wallet
                {
                    Address = wallet,
                    Public = keys.Public,
                    Secret = keys.Secret
                });
                await _context.SaveChangesAsync();
            }
        }

        private async Task GrantDonations(IEnumerable<CartJob.Donor> donors)
        {
            var donations = await _context.Donations.Where(i => !i.Granted && i.Amount > 0).ToListAsync();

            foreach (var item in donations)
            {
                var donor = donors.FirstOrDefault(i => i.Id == item.DonorId && i.Wallet != null);

                if (donor == null) continue;

                if (await _ton.Grant(donor.Wallet, Convert.ToUInt32(item.Amount * 100), 50000000))
                {
                    item.Granted = true;
                    _context.Entry(item).State = EntityState.Modified;
                    await _context.SaveChangesAsync();
                }
            }
        }
        
        private async Task CreateRecipientsWallets()
        {
            var recipients = await GetRecipientsWithoutWallet();

            foreach (var recipient in recipients)
            {
                var keys = await _ton.GenerateKeyPair();

                if (keys == null) continue;

                var wallet = await _ton.DeployWallet(keys.Public, 0);

                if (wallet == null) continue;

                if (!await SaveRecipientWallet(wallet, recipient.Id)) continue;

                await _context.Wallets.AddAsync(new Wallet
                {
                    Address = wallet,
                    Public = keys.Public,
                    Secret = keys.Secret
                });
                await _context.SaveChangesAsync();
            }
        }
        
        public static async Task<List<Donor>> GetDonors()
        {
            //GetDonors
        }

        public static async Task<List<Recipient>> GetRecipientsWithoutWallet()
        {
            //GetRecipientsWithoutWallet
        }

        public static async Task<List<Recipient>> GetRecipients()
        {
            //GetRecipients
        }

        public static async Task<List<CartJob.Donor>> GetDonorsWithoutWallet()
        {
            //GetDonorsWithoutWallet
        }

        public static async Task<bool> SaveRecipientWallet(string address, Guid id)
        {
            //SaveRecipientWallet
        }

        public static async Task<bool> SaveDonorWallet(string address, Guid id)
        {
            //SaveDonorWallet
        }
    }
}
