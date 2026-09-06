using Microsoft.Data.Sqlite;
using Trophy.Catalogue.Domain;

namespace Trophy.Catalogue.Services;

/// <summary>Single-instance operational sidecar. Never reads, replaces or migrates catalogue or identity JSON.</summary>
public sealed class BillingStore
{
    private readonly string connectionString;
    public BillingStore(IWebHostEnvironment environment, IConfiguration configuration)
        : this(Path.Combine(AppDataPath.Resolve(environment, configuration), "operations.sqlite")) { }
    public BillingStore(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        connectionString = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate, DefaultTimeout = 15 }.ToString();
    }

    public Task InitializeAsync()
    {
        using var db = Open();
        Execute(db, null, """
            PRAGMA journal_mode=WAL;
            PRAGMA synchronous=FULL;
            CREATE TABLE IF NOT EXISTS billing_clubs(club_id TEXT PRIMARY KEY, unlimited INTEGER NOT NULL DEFAULT 0, on_hold INTEGER NOT NULL DEFAULT 0, customer_id TEXT);
            CREATE TABLE IF NOT EXISTS credit_ledger(id TEXT PRIMARY KEY, club_id TEXT NOT NULL, delta INTEGER NOT NULL, reason TEXT NOT NULL, created_at INTEGER NOT NULL);
            CREATE INDEX IF NOT EXISTS credit_ledger_club ON credit_ledger(club_id);
            CREATE TABLE IF NOT EXISTS trophy_allocations(club_id TEXT NOT NULL, trophy_id TEXT NOT NULL, state TEXT NOT NULL, PRIMARY KEY(club_id,trophy_id));
            CREATE TABLE IF NOT EXISTS billing_purchases(id TEXT PRIMARY KEY, club_id TEXT NOT NULL, request_id TEXT NOT NULL, pack_code TEXT NOT NULL, credits INTEGER NOT NULL, amount_pence INTEGER NOT NULL, state TEXT NOT NULL, upgrade_from TEXT, checkout_id TEXT UNIQUE, checkout_url TEXT, payment_id TEXT UNIQUE, UNIQUE(club_id,request_id));
            CREATE UNIQUE INDEX IF NOT EXISTS one_upgrade ON billing_purchases(upgrade_from) WHERE upgrade_from IS NOT NULL AND state IN ('pending','paid','review');
            CREATE TABLE IF NOT EXISTS payment_holds(payment_id TEXT PRIMARY KEY, kind TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS stripe_events(id TEXT PRIMARY KEY, event_type TEXT NOT NULL, created_at INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS integration_subscriptions(subscription_id TEXT PRIMARY KEY, club_id TEXT NOT NULL, status TEXT NOT NULL, active_until INTEGER NOT NULL, price_id TEXT NOT NULL, updated_at INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS billable_jobs(id TEXT PRIMARY KEY, club_id TEXT NOT NULL, trophy_id TEXT NOT NULL, kind TEXT NOT NULL, state TEXT NOT NULL, message TEXT NOT NULL, due_at INTEGER NOT NULL, updated_at INTEGER NOT NULL, evidence_count INTEGER NOT NULL DEFAULT 0);
            CREATE UNIQUE INDEX IF NOT EXISTS one_active_job ON billable_jobs(club_id,trophy_id,kind) WHERE state IN ('queued','running','needs_review');
            CREATE INDEX IF NOT EXISTS jobs_due ON billable_jobs(kind,state,due_at);
            CREATE TABLE IF NOT EXISTS ai_attempts(job_id TEXT PRIMARY KEY, club_id TEXT NOT NULL, trophy_id TEXT NOT NULL, kind TEXT NOT NULL, created_at INTEGER NOT NULL);
            """);
        // A crash after sending a request has an unknown provider outcome. Never blindly replay it.
        Execute(db, null, "UPDATE billable_jobs SET state='needs_review',message='This request was interrupted. Its provider outcome needs review before retrying.',updated_at=$now WHERE state='running'", ("$now", Now));
        return Task.CompletedTask;
    }

    public void EnsureClub(string clubId, bool trustedUnlimited = false) => Write((db, tx) =>
    {
        Execute(db, tx, "INSERT OR IGNORE INTO billing_clubs(club_id,unlimited) VALUES($club,$unlimited)", ("$club", clubId), ("$unlimited", trustedUnlimited ? 1 : 0));
        if (trustedUnlimited) Execute(db, tx, "UPDATE billing_clubs SET unlimited=1 WHERE club_id=$club", ("$club", clubId));
        Execute(db, tx, "INSERT OR IGNORE INTO credit_ledger(id,club_id,delta,reason,created_at) VALUES($id,$club,1,'First trophy proof',$now)", ("$id", "proof:" + clubId), ("$club", clubId), ("$now", Now));
        return 0;
    });

    public BillingBalance Balance(string clubId)
    {
        using var db = Open();
        using var tx = db.BeginTransaction(deferred: true);
        return Balance(db, tx, clubId);
    }

    public void SetCustomer(string clubId, string customerId) => Write((db, tx) =>
    {
        Execute(db, tx, "UPDATE billing_clubs SET customer_id=COALESCE(customer_id,$customer) WHERE club_id=$club", ("$customer", customerId), ("$club", clubId));
        return 0;
    });

    public void SyncSubscription(string subscriptionId, string clubId, string status, long activeUntil, string priceId) => Write((db, tx) =>
    {
        Execute(db, tx, "INSERT INTO integration_subscriptions(subscription_id,club_id,status,active_until,price_id,updated_at) VALUES($id,$club,$status,$until,$price,$now) ON CONFLICT(subscription_id) DO UPDATE SET status=$status,active_until=$until,price_id=$price,updated_at=$now WHERE club_id=$club",
            ("$id", subscriptionId), ("$club", clubId), ("$status", status), ("$until", activeUntil), ("$price", priceId), ("$now", Now));
        return 0;
    });

    public bool HasActiveIntegration(string clubId, string priceId)
    {
        using var db = Open();
        return Scalar(db, null, "SELECT COUNT(*) FROM integration_subscriptions WHERE club_id=$club AND price_id=$price AND status='active' AND active_until>$now", ("$club", clubId), ("$price", priceId), ("$now", DateTimeOffset.UtcNow.ToUnixTimeSeconds())) > 0;
    }
    public IReadOnlyList<BillingPurchase> Purchases(string clubId)
    {
        using var db = Open();
        using var cmd = Command(db, null, "SELECT * FROM billing_purchases WHERE club_id=$club ORDER BY rowid DESC LIMIT 100", ("$club", clubId));
        using var reader = cmd.ExecuteReader();
        var result = new List<BillingPurchase>();
        while (reader.Read()) result.Add(Purchase(reader));
        return result;
    }

    public BillingQuote Quote(string clubId, string packCode, string? upgradeFrom) => Write((db, tx) => Quote(db, tx, clubId, packCode, upgradeFrom));
    public BillingPurchase CreatePurchase(string clubId, BillingCheckoutInput input) => Write((db, tx) =>
    {
        if (!Guid.TryParse(input.RequestId, out _)) throw new BillingException("invalid_request", "A valid checkout request identifier is required.", 400);
        using (var existingCommand = Command(db, tx, "SELECT * FROM billing_purchases WHERE club_id=$club AND request_id=$request", ("$club", clubId), ("$request", input.RequestId)))
        using (var existingReader = existingCommand.ExecuteReader())
            if (existingReader.Read())
            {
                var existing = Purchase(existingReader);
                if (existing.PackCode != input.PackCode || existing.UpgradeFrom != input.UpgradeFrom) throw new BillingException("request_conflict", "This checkout identifier was already used for another selection.");
                return existing;
            }
        if (Balance(db, tx, clubId).OnHold) throw new BillingException("billing_review", "Billing is awaiting review. Contact support before making another purchase.");
        var quote = Quote(db, tx, clubId, input.PackCode, input.UpgradeFrom);
        var id = Guid.NewGuid().ToString("N");
        Execute(db, tx, "INSERT INTO billing_purchases(id,club_id,request_id,pack_code,credits,amount_pence,state,upgrade_from) VALUES($id,$club,$request,$pack,$credits,$amount,'pending',$parent)",
            ("$id", id), ("$club", clubId), ("$request", input.RequestId), ("$pack", quote.PackCode), ("$credits", quote.Credits), ("$amount", quote.AmountPence), ("$parent", quote.UpgradeFrom));
        return new BillingPurchase(id, clubId, quote.PackCode, quote.Credits, quote.AmountPence, "pending", quote.UpgradeFrom, null, null, null);
    });

    public BillingPurchase? FindPurchase(string id)
    {
        using var db = Open();
        return FindPurchase(db, null, id);
    }

    public void AttachCheckout(string purchaseId, string checkoutId, string checkoutUrl) => Write((db, tx) =>
    {
        Execute(db, tx, "UPDATE billing_purchases SET checkout_id=$session,checkout_url=$url WHERE id=$id AND state='pending' AND (checkout_id IS NULL OR checkout_id=$session)",
            ("$session", checkoutId), ("$url", checkoutUrl), ("$id", purchaseId));
        return 0;
    });

    public void FulfilPayment(string eventId, string purchaseId, string checkoutId, string paymentId, long amountPence, string currency, string customerId) => Write((db, tx) =>
    {
        if (EventExists(db, tx, eventId)) return 0;
        var purchase = FindPurchase(db, tx, purchaseId) ?? throw new BillingException("unknown_purchase", "The payment does not match an order.", 400);
        if (purchase.AmountPence != amountPence || currency != "gbp" || string.IsNullOrWhiteSpace(paymentId) || (purchase.CheckoutId != null && purchase.CheckoutId != checkoutId))
            throw new BillingException("payment_mismatch", "The payment does not match the stored order.", 400);
        if (purchase.State == "paid") { RecordEvent(db, tx, eventId, "paid"); return 0; }
        if (purchase.State != "pending") { RecordEvent(db, tx, eventId, "late_payment_review"); Execute(db, tx, "UPDATE billing_clubs SET on_hold=1 WHERE club_id=$club", ("$club", purchase.ClubId)); return 0; }
        var held = Scalar(db, tx, "SELECT COUNT(*) FROM payment_holds WHERE payment_id=$payment", ("$payment", paymentId)) > 0;
        Execute(db, tx, "UPDATE billing_purchases SET state=$state,checkout_id=$checkout,payment_id=$payment WHERE id=$id", ("$state", held ? "review" : "paid"), ("$checkout", checkoutId), ("$payment", paymentId), ("$id", purchaseId));
        Execute(db, tx, "UPDATE billing_clubs SET customer_id=COALESCE(customer_id,$customer),on_hold=MAX(on_hold,$held) WHERE club_id=$club", ("$customer", customerId), ("$held", held ? 1 : 0), ("$club", purchase.ClubId));
        if (!held) Execute(db, tx, "INSERT OR IGNORE INTO credit_ledger(id,club_id,delta,reason,created_at) VALUES($id,$club,$credits,'Paid trophy credits',$now)", ("$id", "purchase:" + purchaseId), ("$club", purchase.ClubId), ("$credits", purchase.Credits), ("$now", Now));
        RecordEvent(db, tx, eventId, held ? "payment_review" : "paid");
        return 0;
    });

    public void ExpirePurchase(string eventId, string purchaseId) => Write((db, tx) =>
    {
        if (!EventExists(db, tx, eventId))
        {
            Execute(db, tx, "UPDATE billing_purchases SET state='expired' WHERE id=$id AND state='pending'", ("$id", purchaseId));
            RecordEvent(db, tx, eventId, "expired");
        }
        return 0;
    });

    // Refunds/disputes do not erase trophy records. Freeze new spending for operator review.
    // Store the hold even if this event arrives before checkout fulfilment.
    public void HoldPayment(string eventId, string paymentId, string kind, bool fullRefund) => Write((db, tx) =>
    {
        if (EventExists(db, tx, eventId)) return 0;
        Execute(db, tx, "INSERT OR IGNORE INTO payment_holds(payment_id,kind) VALUES($payment,$kind)", ("$payment", paymentId), ("$kind", kind));
        using var cmd = Command(db, tx, "SELECT * FROM billing_purchases WHERE payment_id=$payment", ("$payment", paymentId));
        BillingPurchase? purchase;
        using (var reader = cmd.ExecuteReader()) purchase = reader.Read() ? Purchase(reader) : null;
        if (purchase != null)
        {
            Execute(db, tx, "UPDATE billing_clubs SET on_hold=1 WHERE club_id=$club", ("$club", purchase.ClubId));
            if (fullRefund && Scalar(db, tx, "SELECT COUNT(*) FROM credit_ledger WHERE id=$id", ("$id", "purchase:" + purchase.Id)) > 0) Execute(db, tx, "INSERT OR IGNORE INTO credit_ledger(id,club_id,delta,reason,created_at) VALUES($id,$club,$credits,'Refunded trophy credits',$now)", ("$id", "refund:" + paymentId), ("$club", purchase.ClubId), ("$credits", -purchase.Credits), ("$now", Now));
            Execute(db, tx, "UPDATE billing_purchases SET state='review' WHERE id=$id", ("$id", purchase.Id));
        }
        RecordEvent(db, tx, eventId, kind);
        return 0;
    });

    public IReadOnlyList<DurableBillableJob> ReviewJobs(string clubId)
    {
        using var db = Open();
        using var cmd = Command(db, null, "SELECT * FROM billable_jobs WHERE club_id=$club AND state='needs_review' ORDER BY updated_at", ("$club", clubId));
        using var reader = cmd.ExecuteReader();
        var jobs = new List<DurableBillableJob>();
        while (reader.Read()) jobs.Add(Job(reader));
        return jobs;
    }

    public void AcknowledgeUnknownJob(string clubId, string jobId) => Write((db, tx) =>
    {
        using var cmd = Command(db, tx, "SELECT * FROM billable_jobs WHERE id=$id AND club_id=$club", ("$id", jobId), ("$club", clubId));
        DurableBillableJob? job;
        using (var reader = cmd.ExecuteReader()) job = reader.Read() ? Job(reader) : null;
        if (job == null) throw new BillingException("job_missing", "This job does not belong to your club.", 404);
        if (job.State == "review_acknowledged") return 0;
        if (job.State != "needs_review") throw new BillingException("review_unavailable", "Only an interrupted job can be acknowledged.");
        Execute(db, tx, "UPDATE billable_jobs SET state='review_acknowledged',message='Club owner reviewed the interrupted request. The attempt remains counted; a fresh request may be made within the allowance.',updated_at=$now WHERE id=$id", ("$id", jobId), ("$now", Now));
        Execute(db, tx, "DELETE FROM trophy_allocations WHERE club_id=$club AND trophy_id=$trophy AND state='reserved' AND NOT EXISTS(SELECT 1 FROM billable_jobs WHERE club_id=$club AND trophy_id=$trophy AND state IN ('queued','running','needs_review'))", ("$club", clubId), ("$trophy", job.TrophyId));
        return 0;
    });
    public void CheckPhotoAllowance(string clubId, string trophyId, int totalPhotoCount)
    {
        using var db = Open();
        using var tx = db.BeginTransaction(deferred: true);
        CheckAllowance(db, tx, clubId, trophyId, "photo", totalPhotoCount);
    }

    public DurableBillableJob ScheduleJob(string clubId, string trophyId, string kind, int evidenceCount, DateTimeOffset dueAt) => Write((db, tx) =>
    {
        if (kind is not ("analysis" or "illustration")) throw new ArgumentException("Unknown job kind.", nameof(kind));
        using (var cmd = Command(db, tx, "SELECT * FROM billable_jobs WHERE club_id=$club AND trophy_id=$trophy AND kind=$kind AND state IN ('queued','running','needs_review')", ("$club", clubId), ("$trophy", trophyId), ("$kind", kind)))
        {
            DurableBillableJob? existing;
            using (var reader = cmd.ExecuteReader()) existing = reader.Read() ? Job(reader) : null;
            if (existing != null)
            {
                if (existing.State == "needs_review") throw new BillingException("provider_outcome_unknown", existing.Message);
                if (existing.State == "queued")
                {
                    Execute(db, tx, "UPDATE billable_jobs SET due_at=$due,evidence_count=$count,updated_at=$now WHERE id=$id", ("$due", dueAt.ToUnixTimeMilliseconds()), ("$count", evidenceCount), ("$now", Now), ("$id", existing.Id));
                    return existing with { DueAt = dueAt, EvidenceCount = evidenceCount };
                }
                return existing;
            }
        }
        CheckAllowance(db, tx, clubId, trophyId, kind, evidenceCount);
        ReserveTrophy(db, tx, clubId, trophyId);
        var job = new DurableBillableJob(Guid.NewGuid().ToString("N"), clubId, trophyId, kind, "queued", "Saved and queued for processing.", dueAt, DateTimeOffset.UtcNow, evidenceCount);
        Execute(db, tx, "INSERT INTO billable_jobs(id,club_id,trophy_id,kind,state,message,due_at,updated_at,evidence_count) VALUES($id,$club,$trophy,$kind,'queued',$message,$due,$now,$count)",
            ("$id", job.Id), ("$club", clubId), ("$trophy", trophyId), ("$kind", kind), ("$message", job.Message), ("$due", dueAt.ToUnixTimeMilliseconds()), ("$now", Now), ("$count", evidenceCount));
        return job;
    });

    public DurableBillableJob? NextJob(string kind)
    {
        using var db = Open();
        using var cmd = Command(db, null, "SELECT * FROM billable_jobs WHERE kind=$kind AND state='queued' AND due_at <= $now ORDER BY due_at LIMIT 1", ("$kind", kind), ("$now", Now));
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Job(reader) : null;
    }

    public DurableBillableJob? JobStatus(string clubId, string trophyId, string kind)
    {
        using var db = Open();
        using var cmd = Command(db, null, "SELECT * FROM billable_jobs WHERE club_id=$club AND trophy_id=$trophy AND kind=$kind ORDER BY rowid DESC LIMIT 1", ("$club", clubId), ("$trophy", trophyId), ("$kind", kind));
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Job(reader) : null;
    }

    // Called immediately before the provider call, after loading and checking the saved inputs.
    public bool BeginProviderAttempt(DurableBillableJob job, int evidenceCount) => Write((db, tx) =>
    {
        if (Scalar(db, tx, "SELECT COUNT(*) FROM billable_jobs WHERE id=$id AND state='queued'", ("$id", job.Id)) == 0) return false;
        CheckAllowance(db, tx, job.ClubId, job.TrophyId, job.Kind, evidenceCount);
        var changed = Execute(db, tx, "UPDATE billable_jobs SET state='running',message='Processing saved photographs.',updated_at=$now WHERE id=$id AND state='queued'", ("$id", job.Id), ("$now", Now));
        if (changed == 0) return false;
        Execute(db, tx, "INSERT INTO ai_attempts(job_id,club_id,trophy_id,kind,created_at) VALUES($id,$club,$trophy,$kind,$now)", ("$id", job.Id), ("$club", job.ClubId), ("$trophy", job.TrophyId), ("$kind", job.Kind), ("$now", Now));
        return true;
    });

    public void CompleteJob(DurableBillableJob job, string message) => Write((db, tx) =>
    {
        var unlimited = Balance(db, tx, job.ClubId).Unlimited;
        if (!unlimited) Execute(db, tx, "INSERT OR IGNORE INTO credit_ledger(id,club_id,delta,reason,created_at) VALUES($id,$club,-1,'Trophy processing completed',$now)", ("$id", "trophy:" + job.ClubId + ":" + job.TrophyId), ("$club", job.ClubId), ("$now", Now));
        Execute(db, tx, "UPDATE trophy_allocations SET state='settled' WHERE club_id=$club AND trophy_id=$trophy", ("$club", job.ClubId), ("$trophy", job.TrophyId));
        Execute(db, tx, "UPDATE billable_jobs SET state='complete',message=$message,updated_at=$now WHERE id=$id", ("$message", message), ("$now", Now), ("$id", job.Id));
        return 0;
    });

    public void FailJob(DurableBillableJob job, string message, bool providerOutcomeUnknown) => Write((db, tx) =>
    {
        Execute(db, tx, "UPDATE billable_jobs SET state=$state,message=$message,updated_at=$now WHERE id=$id AND state IN ('queued','running')", ("$state", providerOutcomeUnknown ? "needs_review" : "failed"), ("$message", message), ("$now", Now), ("$id", job.Id));
        if (!providerOutcomeUnknown) Execute(db, tx, "DELETE FROM trophy_allocations WHERE club_id=$club AND trophy_id=$trophy AND state='reserved' AND NOT EXISTS(SELECT 1 FROM billable_jobs WHERE club_id=$club AND trophy_id=$trophy AND state IN ('queued','running','needs_review'))", ("$club", job.ClubId), ("$trophy", job.TrophyId));
        return 0;
    });

    private static BillingQuote Quote(SqliteConnection db, SqliteTransaction tx, string clubId, string packCode, string? upgradeFrom)
    {
        var pack = TrophyCreditPack.Find(packCode);
        if (upgradeFrom is null) return new(pack.Code, pack.Credits, pack.AmountPence, "gbp", null);
        var previous = FindPurchase(db, tx, upgradeFrom);
        if (previous is null || previous.ClubId != clubId || previous.State != "paid") throw new BillingException("invalid_upgrade", "Choose a paid pack belonging to this club.");
        var sourcePack = TrophyCreditPack.Find(previous.PackCode);
        if (sourcePack.Credits >= pack.Credits || Scalar(db, tx, "SELECT COUNT(*) FROM billing_purchases WHERE upgrade_from=$parent AND state IN ('pending','paid','review')", ("$parent", upgradeFrom)) > 0)
            throw new BillingException("upgrade_unavailable", "This pack already has an upgrade or is larger than your selection.");
        return new(pack.Code, pack.Credits - sourcePack.Credits, pack.AmountPence - sourcePack.AmountPence, "gbp", upgradeFrom);
    }

    private static void ReserveTrophy(SqliteConnection db, SqliteTransaction tx, string clubId, string trophyId)
    {
        if (Scalar(db, tx, "SELECT COUNT(*) FROM trophy_allocations WHERE club_id=$club AND trophy_id=$trophy", ("$club", clubId), ("$trophy", trophyId)) > 0) return;
        var balance = Balance(db, tx, clubId);
        if (!balance.Unlimited && balance.Available < 1) throw new BillingException("credits_required", "Add trophy credits before processing another trophy.", 402);
        Execute(db, tx, "INSERT INTO trophy_allocations(club_id,trophy_id,state) VALUES($club,$trophy,'reserved')", ("$club", clubId), ("$trophy", trophyId));
    }

    private static void CheckAllowance(SqliteConnection db, SqliteTransaction tx, string clubId, string trophyId, string kind, int evidenceCount)
    {
        var balance = Balance(db, tx, clubId);
        if (balance.OnHold) throw new BillingException("billing_review", "New AI work is paused while a billing issue is reviewed.");
        if (balance.Unlimited) return;
        var paid = Scalar(db, tx, "SELECT COUNT(*) FROM credit_ledger WHERE club_id=$club AND reason='Paid trophy credits' AND delta>0", ("$club", clubId)) > 0;
        var photoLimit = paid ? 40 : 12;
        if (evidenceCount > photoLimit) throw new BillingException("photo_limit", $"The current allowance is {photoLimit} saved photographs per trophy.", 402);
        if (kind == "photo") return;
        var limit = kind == "analysis" ? (paid ? 12 : 3) : (paid ? 3 : 2);
        var attempts = Scalar(db, tx, "SELECT COUNT(*) FROM ai_attempts WHERE club_id=$club AND trophy_id=$trophy AND kind=$kind", ("$club", clubId), ("$trophy", trophyId), ("$kind", kind));
        if (attempts >= limit) throw new BillingException("ai_allowance_used", $"This trophy has reached its {limit} {kind} attempts. Contact support for a review.", 402);
    }

    private static BillingBalance Balance(SqliteConnection db, SqliteTransaction tx, string clubId)
    {
        bool unlimited, held; string? customer;
        using (var cmd = Command(db, tx, "SELECT unlimited,on_hold,customer_id FROM billing_clubs WHERE club_id=$club", ("$club", clubId)))
        using (var reader = cmd.ExecuteReader())
        {
            if (!reader.Read()) throw new BillingException("billing_not_initialized", "The club billing account is not initialized.", 503);
            unlimited = reader.GetInt64(0) != 0; held = reader.GetInt64(1) != 0; customer = reader.IsDBNull(2) ? null : reader.GetString(2);
        }
        var total = Scalar(db, tx, "SELECT COALESCE(SUM(delta),0) FROM credit_ledger WHERE club_id=$club", ("$club", clubId));
        var reserved = Scalar(db, tx, "SELECT COUNT(*) FROM trophy_allocations WHERE club_id=$club AND state='reserved'", ("$club", clubId));
        var used = Scalar(db, tx, "SELECT COUNT(*) FROM trophy_allocations WHERE club_id=$club AND state='settled'", ("$club", clubId));
        return new(clubId, unlimited, unlimited ? 0 : total - reserved, reserved, used, held, customer);
    }

    private static BillingPurchase? FindPurchase(SqliteConnection db, SqliteTransaction? tx, string id)
    {
        using var cmd = Command(db, tx, "SELECT * FROM billing_purchases WHERE id=$id", ("$id", id));
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Purchase(reader) : null;
    }
    private static BillingPurchase Purchase(SqliteDataReader r) => new(S(r,"id")!, S(r,"club_id")!, S(r,"pack_code")!, Convert.ToInt32(r["credits"]), Convert.ToInt64(r["amount_pence"]), S(r,"state")!, S(r,"upgrade_from"), S(r,"checkout_id"), S(r,"checkout_url"), S(r,"payment_id"), S(r,"request_id"));
    private static DurableBillableJob Job(SqliteDataReader r) => new(S(r,"id")!, S(r,"club_id")!, S(r,"trophy_id")!, S(r,"kind")!, S(r,"state")!, S(r,"message")!, DateTimeOffset.FromUnixTimeMilliseconds(Convert.ToInt64(r["due_at"])), DateTimeOffset.FromUnixTimeMilliseconds(Convert.ToInt64(r["updated_at"])), Convert.ToInt32(r["evidence_count"]));
    private static string? S(SqliteDataReader r, string name) => r[name] is DBNull ? null : Convert.ToString(r[name]);
    private static long Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    private SqliteConnection Open() { var db = new SqliteConnection(connectionString); db.Open(); Execute(db, null, "PRAGMA busy_timeout=15000; PRAGMA foreign_keys=ON; PRAGMA synchronous=FULL;"); return db; }
    private T Write<T>(Func<SqliteConnection, SqliteTransaction, T> action) { using var db = Open(); using var tx = db.BeginTransaction(deferred: false); var result = action(db, tx); tx.Commit(); return result; }
    private static SqliteCommand Command(SqliteConnection db, SqliteTransaction? tx, string sql, params (string Name, object? Value)[] values) { var cmd = db.CreateCommand(); cmd.Transaction = tx; cmd.CommandText = sql; foreach (var value in values) cmd.Parameters.AddWithValue(value.Name, value.Value ?? DBNull.Value); return cmd; }
    private static int Execute(SqliteConnection db, SqliteTransaction? tx, string sql, params (string Name, object? Value)[] values) { using var cmd = Command(db, tx, sql, values); return cmd.ExecuteNonQuery(); }
    private static long Scalar(SqliteConnection db, SqliteTransaction? tx, string sql, params (string Name, object? Value)[] values) { using var cmd = Command(db, tx, sql, values); return Convert.ToInt64(cmd.ExecuteScalar()); }
    private static bool EventExists(SqliteConnection db, SqliteTransaction tx, string id) => Scalar(db, tx, "SELECT COUNT(*) FROM stripe_events WHERE id=$id", ("$id", id)) > 0;
    private static void RecordEvent(SqliteConnection db, SqliteTransaction tx, string id, string type) => Execute(db, tx, "INSERT INTO stripe_events(id,event_type,created_at) VALUES($id,$type,$now)", ("$id", id), ("$type", type), ("$now", Now));
}
