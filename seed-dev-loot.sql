-- ============================================================================
-- seed-dev-loot.sql  —  Local dev seed data for the character drop-log dashboard
-- ============================================================================
--
-- Generates several GameCharacters and a few thousand realistic LootRecords
-- (kills + JSONB drop tables) so the /loot/log/{id} dashboard, source cards,
-- dry streaks, first-time highlights, the luck leaderboard and feed tiers all
-- have data to render.
--
-- HOW IT WORKS
--   * Data is attached to an EXISTING user (admin preferred, else lowest Id) so
--     you log in with your normal credentials and the characters belong to you
--     (CharacterAccessChecker requires ownership / admin). Passwords are ASP.NET
--     Identity hashes and cannot be seeded from SQL, so we never create a login.
--   * Seeded rows are tagged via GameCharacters."RuneLiteId" LIKE 'seed-%' and
--     wiped at the top, so the script is safe to re-run (idempotent).
--
-- PREREQUISITES
--   Start the app once (npm run dev) so migrations run and the system user is
--   created. Then run this against the local Postgres from compose.yaml:
--
--     docker compose exec -T db psql -U postgres -d klavlor < seed-dev-loot.sql
--   or
--     psql "postgresql://postgres:postgres@localhost:5430/klavlor" -f seed-dev-loot.sql
--
-- VOLUME
--   Rates below are roughly real OSRS drop chances, so the kill counts are large
--   (a veteran-scale grind) to make uniques actually surface. This produces on
--   the order of ~18k records and takes a little while; scale the per-character
--   weights or the per-source kills down if you want it lighter.
-- ============================================================================

BEGIN;

-- ----------------------------------------------------------------------------
-- 1. Resolve the target user (admin first, then lowest Id).
-- ----------------------------------------------------------------------------
CREATE TEMP TABLE seed_ctx ON COMMIT DROP AS
SELECT u."Id" AS user_id
FROM "Users" u
LEFT JOIN "UserRole" ur ON ur."UserId" = u."Id"
LEFT JOIN "Roles"    r  ON r."Id" = ur."RoleId" AND r."Name" = 'Admin'
ORDER BY (r."Id" IS NULL), u."Id"   -- admins (FALSE=0) first, then lowest Id
LIMIT 1;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM seed_ctx) THEN
        RAISE EXCEPTION 'No users found. Start the app once so the system user is created, then re-run.';
    END IF;
    RAISE NOTICE 'Seeding loot data onto user Id %', (SELECT user_id FROM seed_ctx);
END $$;

-- ----------------------------------------------------------------------------
-- 2. Clean previous seed (idempotent re-run).
-- ----------------------------------------------------------------------------
DELETE FROM "LootRecords"
WHERE "GameCharacterId" IN (SELECT "Id" FROM "GameCharacters" WHERE "RuneLiteId" LIKE 'seed-%');

DELETE FROM "GameCharacters" WHERE "RuneLiteId" LIKE 'seed-%';

-- ----------------------------------------------------------------------------
-- 3. Seed characters, each with a volume weight so the three accounts have
--    genuinely different progression rather than identical profiles. The weight
--    scales every source's kill count: the veteran owns most collectibles (and
--    is the one high enough KC to rack up real dry streaks on the near-misses),
--    the mid account is partway, and the fresh account is mostly still chasing.
-- ----------------------------------------------------------------------------
CREATE TEMP TABLE seed_char (rl text, dn text, weight numeric) ON COMMIT DROP;
INSERT INTO seed_char VALUES
    ('seed-claudelock', 'ClaudeLock',  1.00),  -- veteran: high KC, most uniques
    ('seed-zukkenmir',  'Zukkenmir',   0.40),  -- mid-tier
    ('seed-ironfodder', 'Iron Fodder', 0.12);  -- fresh, mostly dry / still chasing

INSERT INTO "GameCharacters"
    ("UserId", "RuneLiteId", "DisplayName", "IsVisible", "IsAdminHidden", "SavedAt", "SavedById")
SELECT s.user_id, c.rl, c.dn, true, false, now(), s.user_id
FROM seed_ctx s
CROSS JOIN seed_char c;

-- ----------------------------------------------------------------------------
-- 4. Source definitions: (name, LootSourceType, combat level, veteran kills).
--    combat 0 -> stored as NULL (raids/events have no combat level). The kills
--    column is the veteran (weight 1.0) volume; other characters get a fraction.
-- ----------------------------------------------------------------------------
CREATE TEMP TABLE seed_source (source text, stype text, combat int, kills int) ON COMMIT DROP;
INSERT INTO seed_source VALUES
    ('Vorkath',           'Npc',         732, 1300),
    ('Zulrah',            'Npc',         725, 1500),
    ('Alchemical Hydra',  'Npc',         426, 1150),
    ('Cerberus',          'Npc',         318, 1150),
    ('General Graardor',  'Npc',         624,  520),
    ('Kree''arra',        'Npc',         580,  430),
    ('Corporeal Beast',   'Npc',         785,  360),
    ('Nex',               'Npc',        1001,  380),
    ('Vardorvis',         'Npc',         784,  540),
    ('Chambers of Xeric', 'Event',         0,  520),
    ('Theatre of Blood',  'Event',         0,  430),
    ('Tombs of Amascut',  'Event',         0,  560),
    ('Master Farmer',     'Pickpocket',   38, 3000),
    ('PvP Kill',          'Player',      126,  150),
    ('Doom of Mokhaiotl', 'Npc',           0,  320);

-- ----------------------------------------------------------------------------
-- 5. Drop tables: (source, item name, OSRS item id, unit GE price, max qty, drop chance)
--    Every source has at least one prob=1.0 "guaranteed" common so no kill is empty.
--    Probabilities are now roughly the real OSRS rates, so at the veteran volumes
--    above you get a believable mix: the common-ish uniques are usually owned (some
--    early = spoons), the rarer ones are hit-or-miss, and a high-KC near-miss reads
--    as a genuine dry streak on the leaderboard, which computes luck against the
--    real wiki rates from the drop-rate sync.
-- ----------------------------------------------------------------------------
CREATE TEMP TABLE seed_drop (source text, name text, item_id int, unit_price bigint, max_qty int, prob double precision) ON COMMIT DROP;
INSERT INTO seed_drop VALUES
    -- Vorkath
    ('Vorkath', 'Superior dragon bones', 22124,     8000, 30, 1.00),
    ('Vorkath', 'Blue dragonhide',        1751,     1600, 30, 1.00),
    ('Vorkath', 'Dragonbone necklace',   22095,    90000,  1, 0.00100),
    ('Vorkath', 'Jar of decay',          22106,   120000,  1, 0.00033),
    ('Vorkath', 'Skeletal visage',       22006, 55000000,  1, 0.00020),
    ('Vorkath', 'Vorki',                 21992,        0,  1, 0.00033),
    -- Zulrah
    ('Zulrah', 'Zulrah''s scales',       12934,      250, 1000, 1.00),
    ('Zulrah', 'Snakeskin',               6289,      200,   50, 0.80),
    ('Zulrah', 'Tanzanite fang',         12922,  3200000,    1, 0.00195),
    ('Zulrah', 'Magic fang',             12932,  2800000,    1, 0.00195),
    ('Zulrah', 'Serpentine visage',      12927,  6500000,    1, 0.00195),
    ('Zulrah', 'Jar of swamp',           12936,   120000,    1, 0.00007),
    ('Zulrah', 'Pet snakeling',          12921,        0,    1, 0.00025),
    -- Alchemical Hydra
    ('Alchemical Hydra', 'Hydra leather', 22983,  110000,   1, 1.00),
    ('Alchemical Hydra', 'Dragon thrownaxe', 21205,  100, 200, 0.70),
    ('Alchemical Hydra', 'Hydra''s claw', 22966, 1200000,   1, 0.00100),
    ('Alchemical Hydra', 'Hydra tail',    22988,   90000,   1, 0.00195),
    ('Alchemical Hydra', 'Brimstone ring', 22975, 800000,   1, 0.00050),
    ('Alchemical Hydra', 'Ikkle hydra',   22746,       0,   1, 0.00033),
    -- Cerberus
    ('Cerberus', 'Infernal ashes',       25775,    1500,  5, 1.00),
    ('Cerberus', 'Key master teleport',  13249,    2000,  3, 0.50),
    ('Cerberus', 'Primordial crystal',   13231, 3000000,  1, 0.00195),
    ('Cerberus', 'Pegasian crystal',     13229, 2500000,  1, 0.00195),
    ('Cerberus', 'Eternal crystal',      13227, 2800000,  1, 0.00195),
    ('Cerberus', 'Smouldering stone',    13233,  450000,  1, 0.00195),
    ('Cerberus', 'Hellpuppy',            13247,       0,  1, 0.00033),
    -- General Graardor
    ('General Graardor', 'Coins',           995,        1, 50000, 1.00),
    ('General Graardor', 'Bandos boots',  11836,   250000,     1, 0.00260),
    ('General Graardor', 'Bandos chestplate', 11832, 17000000, 1, 0.00197),
    ('General Graardor', 'Bandos tassets', 11834, 22000000,    1, 0.00197),
    ('General Graardor', 'Bandos hilt',   11812,  8000000,     1, 0.00197),
    ('General Graardor', 'Pet general graardor', 12650, 0,     1, 0.00020),
    -- Kree'arra
    ('Kree''arra', 'Coins',                 995,        1, 40000, 1.00),
    ('Kree''arra', 'Armadyl helmet',      11826,  1200000,     1, 0.00197),
    ('Kree''arra', 'Armadyl chestplate',  11828, 12000000,     1, 0.00197),
    ('Kree''arra', 'Armadyl chainskirt',  11830,  9000000,     1, 0.00197),
    ('Kree''arra', 'Armadyl hilt',        11810,  4000000,     1, 0.00197),
    ('Kree''arra', 'Pet kree''arra',      12649,        0,     1, 0.00020),
    -- Corporeal Beast
    ('Corporeal Beast', 'Spirit shield',  12829,    35000, 1, 1.00),
    ('Corporeal Beast', 'Holy elixir',    12833,   350000, 1, 0.01560),
    ('Corporeal Beast', 'Spectral sigil', 12821, 60000000, 1, 0.00073),
    ('Corporeal Beast', 'Arcane sigil',   12825, 18000000, 1, 0.00073),
    ('Corporeal Beast', 'Elysian sigil',  12819, 700000000,1, 0.00024),
    ('Corporeal Beast', 'Pet dark core',  13182,        0, 1, 0.00020),
    -- Nex
    ('Nex', 'Nihil shard',               26211,     4000, 30, 1.00),
    ('Nex', 'Torva full helm',           26382, 38000000,  1, 0.00290),
    ('Nex', 'Torva platebody',           26384, 50000000,  1, 0.00290),
    ('Nex', 'Torva platelegs',           26386, 45000000,  1, 0.00290),
    ('Nex', 'Nihil horn',                26372, 14000000,  1, 0.00580),
    ('Nex', 'Zaryte vambraces',          26235,  4500000,  1, 0.01160),
    ('Nex', 'Ancient hilt',              26370, 90000000,  1, 0.00194),
    ('Nex', 'Nexling',                   26348,        0,  1, 0.00200),
    -- Vardorvis
    ('Vardorvis', 'Coins',                  995,        1, 30000, 1.00),
    ('Vardorvis', 'Awakener''s orb',      28334,  1500000,     2, 0.12500),
    ('Vardorvis', 'Virtus mask',          26241, 25000000,     1, 0.00066),
    ('Vardorvis', 'Virtus robe top',      26243, 30000000,     1, 0.00066),
    ('Vardorvis', 'Virtus robe bottom',   26245, 28000000,     1, 0.00066),
    ('Vardorvis', 'Ultor vestige',        28285, 60000000,     1, 0.00116),
    ('Vardorvis', 'Butch',                28250,        0,     1, 0.00033),
    -- Chambers of Xeric (raid). Rates are realistic PER-RAID = unique-table share / ~32 raids
    -- per unique, so characters own a believable subset and the board shows spoons + dry streaks.
    ('Chambers of Xeric', 'Coins',                   995,          1, 200000, 1.00),
    ('Chambers of Xeric', 'Arcane prayer scroll',    21079,    400000,     1, 0.00906),
    ('Chambers of Xeric', 'Dexterous prayer scroll', 21034,    400000,     1, 0.00906),
    ('Chambers of Xeric', 'Twisted buckler',         21000,   4000000,     1, 0.00181),
    ('Chambers of Xeric', 'Dragon hunter crossbow',  21012,   4500000,     1, 0.00181),
    ('Chambers of Xeric', 'Ancestral hat',           21018,  15000000,     1, 0.00136),
    ('Chambers of Xeric', 'Ancestral robe top',      21021,  30000000,     1, 0.00136),
    ('Chambers of Xeric', 'Ancestral robe bottom',   21024,  28000000,     1, 0.00136),
    ('Chambers of Xeric', 'Dinh''s bulwark',         21015,   5000000,     1, 0.00136),
    ('Chambers of Xeric', 'Dragon claws',            13652,  75000000,     1, 0.00136),
    ('Chambers of Xeric', 'Elder maul',              21003,  70000000,     1, 0.00091),
    ('Chambers of Xeric', 'Kodai insignia',          21043,  80000000,     1, 0.00091),
    ('Chambers of Xeric', 'Twisted bow',             20997,1200000000,     1, 0.00091),
    ('Chambers of Xeric', 'Metamorphic dust',        22386,  30000000,     1, 0.00008),
    ('Chambers of Xeric', 'Olmlet',                  20851,         0,     1, 0.00060),
    -- Theatre of Blood (raid). Per-raid-per-player = unique-table share / ~36 (team unique
    -- ~1/9.1 handed to one of ~4 players).
    ('Theatre of Blood', 'Coins',                995,         1, 150000, 1.00),
    ('Theatre of Blood', 'Avernic defender hilt', 22477, 70000000,   1, 0.01111),
    ('Theatre of Blood', 'Ghrazi rapier',      22324,  90000000,     1, 0.00292),
    ('Theatre of Blood', 'Sanguinesti staff (uncharged)', 22481, 75000000, 1, 0.00292),
    ('Theatre of Blood', 'Justiciar faceguard',22326,   8000000,     1, 0.00292),
    ('Theatre of Blood', 'Justiciar chestguard',22327,  9000000,     1, 0.00292),
    ('Theatre of Blood', 'Justiciar legguards',22328,   9000000,     1, 0.00292),
    ('Theatre of Blood', 'Scythe of vitur (uncharged)', 22486, 750000000, 1, 0.00146),
    ('Theatre of Blood', 'Lil'' zik',          22473,         0,     1, 0.00060),
    -- Tombs of Amascut (raid). Per-raid = unique-table share / ~21 raids per unique.
    ('Tombs of Amascut', 'Coins',                995,          1, 120000, 1.00),
    ('Tombs of Amascut', 'Osmumten''s fang',    26219,   25000000,     1, 0.01389),
    ('Tombs of Amascut', 'Lightbearer',         25975,    4000000,     1, 0.01389),
    ('Tombs of Amascut', 'Elidinis'' ward',     25985,   30000000,     1, 0.00595),
    ('Tombs of Amascut', 'Masori mask',         27226,  15000000,     1, 0.00397),
    ('Tombs of Amascut', 'Masori body',         27229,  60000000,     1, 0.00397),
    ('Tombs of Amascut', 'Masori chaps',        27232,  40000000,     1, 0.00397),
    ('Tombs of Amascut', 'Tumeken''s shadow (uncharged)', 27277, 1000000000, 1, 0.00198),
    ('Tombs of Amascut', 'Tumeken''s guardian', 27352,          0,     1, 0.00050),
    -- Master Farmer (pickpocket)
    ('Master Farmer', 'Potato seed',      5318,     5, 5, 1.00),
    ('Master Farmer', 'Ranarr seed',      5295, 35000, 1, 0.02500),
    ('Master Farmer', 'Snapdragon seed',  5300, 50000, 1, 0.01000),
    ('Master Farmer', 'Torstol seed',     5304, 60000, 1, 0.00500),
    ('Master Farmer', 'Magic seed',       5316, 90000, 1, 0.00250),
    -- PvP Kill (player)
    ('PvP Kill', 'Coins',          995,      1, 100000, 1.00),
    ('PvP Kill', 'Looting bag',  11941,   5000,     1, 0.30),
    ('PvP Kill', 'Dragon dagger',  1215,  17000,     1, 0.20),
    -- Doom of Mokhaiotl (delve boss). Loot rolls per delve level and is claimed once;
    -- our SourceLootService / DoomLootStrategy re-derive an estimated delve depth. Demon
    -- tears are guaranteed and their quantity scales with depth (the strategy's stronger
    -- depth signal), so a wide max qty spreads the derived depths across roughly 1 to 8.
    -- The uniques are depth-gated in game; the estimator takes the max of the tear-implied
    -- depth and the gate of any unique present, so independent rolls here still read sensibly.
    -- Item names must contain the exact tokens the strategy matches. Ids/prices are
    -- approximate (recent boss); icons resolve by name, so the ids are cosmetic.
    ('Doom of Mokhaiotl', 'Demon tears',              30626,       800, 450, 1.00),
    ('Doom of Mokhaiotl', 'Mokhaiotl cloth',          30628,    600000,   1, 0.05000),
    ('Doom of Mokhaiotl', 'Eye of ayak (uncharged)',  30622,  45000000,   1, 0.00100),
    ('Doom of Mokhaiotl', 'Avernic treads',           30624, 220000000,   1, 0.00074),
    ('Doom of Mokhaiotl', 'Dom',                       30630,         0,   1, 0.00040);

-- ----------------------------------------------------------------------------
-- 6. Generate one row per (character, source, kill) with a random timestamp over
--    the last 150 days. The per-character weight scales each source's kill count.
-- ----------------------------------------------------------------------------
CREATE TEMP TABLE seed_kill ON COMMIT DROP AS
SELECT
    c."Id"      AS character_id,
    c."UserId"  AS user_id,
    s.source,
    s.stype,
    NULLIF(s.combat, 0) AS combat,
    now() - (random() * interval '150 days') AS occurred_at
FROM "GameCharacters" c
JOIN seed_char ch ON ch.rl = c."RuneLiteId"
CROSS JOIN seed_source s
CROSS JOIN LATERAL generate_series(1, GREATEST(1, round(s.kills * ch.weight)::int)) AS g(n)
WHERE c."RuneLiteId" LIKE 'seed-%';

-- ----------------------------------------------------------------------------
-- 7. Roll drops for each kill, build the DropsJson array and TotalValue.
--    (IsFirstTime is set to false here; corrected in step 9.)
-- ----------------------------------------------------------------------------
CREATE TEMP TABLE seed_kill_drops ON COMMIT DROP AS
SELECT
    k.character_id,
    k.user_id,
    k.source,
    k.stype,
    k.combat,
    k.occurred_at,
    jsonb_agg(
        jsonb_build_object(
            'Name',        d.name,
            'ItemId',      d.item_id,
            'Quantity',    d.qty,
            'Price',       d.unit_price,
            'IsFirstTime', false
        )
        ORDER BY d.unit_price DESC
    ) AS drops,
    SUM(d.unit_price * d.qty) AS total_value
FROM seed_kill k
JOIN LATERAL (
    SELECT
        sd.name,
        sd.item_id,
        sd.unit_price,
        GREATEST(1, (1 + floor(random() * sd.max_qty))::int) AS qty
    FROM seed_drop sd
    WHERE sd.source = k.source
      AND random() < sd.prob
) d ON true
GROUP BY k.character_id, k.user_id, k.source, k.stype, k.combat, k.occurred_at;

-- ----------------------------------------------------------------------------
-- 8. Insert into LootRecords. KillCount is the true kill ordinal per (char,source)
--    starting at 1 — NO synthetic base offset. An offset would inflate every first-drop
--    kill count above its expected value and force the luck leaderboard to read every
--    obtained item as "dry", killing all spoons; the honest ordinal lets early pulls be
--    spoons and only genuine late/missing pulls be dry streaks.
-- ----------------------------------------------------------------------------
INSERT INTO "LootRecords"
    ("UserId", "GameCharacterId", "SourceName", "SourceType", "CombatLevel",
     "KillCount", "TotalValue", "DropsJson", "OccurredAt", "ContentHash",
     "IsImported", "SavedAt", "SavedById")
SELECT
    user_id,
    character_id,
    source,
    stype,
    combat,
    (row_number() OVER (PARTITION BY character_id, source ORDER BY occurred_at))::int AS kill_count,
    total_value,
    drops,
    occurred_at,
    NULL,                 -- ContentHash: unused for seed (dedup is ingest-only)
    false,                -- IsImported: treat as live (publishable) kills
    now(),
    user_id
FROM seed_kill_drops;

-- ----------------------------------------------------------------------------
-- 9. Set IsFirstTime on the earliest occurrence of each item per character,
--    mirroring LootRecordRepository.RecomputeFirstTimeFlags so the dashboard's
--    first-time highlighting is consistent with app logic.
-- ----------------------------------------------------------------------------
WITH unrolled AS (
    SELECT lr."Id" AS rec_id, lr."GameCharacterId" AS cid, lr."OccurredAt" AS t,
           d.elem->>'Name' AS item_name, d.idx
    FROM "LootRecords" lr,
         jsonb_array_elements(lr."DropsJson") WITH ORDINALITY AS d(elem, idx)
    WHERE lr."GameCharacterId" IN (SELECT "Id" FROM "GameCharacters" WHERE "RuneLiteId" LIKE 'seed-%')
),
firsts AS (
    SELECT DISTINCT ON (cid, item_name) rec_id, cid, item_name
    FROM unrolled
    ORDER BY cid, item_name, t, rec_id, idx
)
UPDATE "LootRecords" lr
SET "DropsJson" = (
    SELECT jsonb_agg(
        CASE
            WHEN EXISTS (SELECT 1 FROM firsts f
                         WHERE f.rec_id = lr."Id"
                           AND f.item_name = d.elem->>'Name')
            THEN (d.elem - 'IsFirstTime') || '{"IsFirstTime": true}'::jsonb
            ELSE (d.elem - 'IsFirstTime') || '{"IsFirstTime": false}'::jsonb
        END
        ORDER BY d.idx
    )
    FROM jsonb_array_elements(lr."DropsJson") WITH ORDINALITY AS d(elem, idx)
)
WHERE lr."GameCharacterId" IN (SELECT "Id" FROM "GameCharacters" WHERE "RuneLiteId" LIKE 'seed-%');

-- ----------------------------------------------------------------------------
-- 9b. Build the LootDrops projection from the finalised DropsJson. The app normally
--     writes this on ingest (FinalizeDrops); the seed must too, because every item-level
--     query (GetSourceCollection, the leaderboard, source cards) JOINs this normalised
--     table, not the jsonb. Without it a seeded character reads as having obtained nothing
--     and missing everything — all dry streaks, no spoons. Cascade-deletes with the record.
-- ----------------------------------------------------------------------------
INSERT INTO "LootDrops" ("LootRecordId", "ItemId", "Name", "Quantity", "Price", "IsFirstTime")
SELECT lr."Id",
       COALESCE((d->>'ItemId')::int, 0),
       COALESCE(d->>'Name', ''),
       COALESCE((d->>'Quantity')::int, 0),
       COALESCE((d->>'Price')::int, 0),
       COALESCE((d->>'IsFirstTime')::boolean, false)
FROM "LootRecords" lr,
     LATERAL jsonb_array_elements(lr."DropsJson") AS d
WHERE lr."GameCharacterId" IN (SELECT "Id" FROM "GameCharacters" WHERE "RuneLiteId" LIKE 'seed-%');

-- ----------------------------------------------------------------------------
-- 10. Report what was created (character Id -> use in /loot/log/{Id}).
-- ----------------------------------------------------------------------------
SELECT gc."Id" AS character_id,
       gc."DisplayName",
       count(lr."Id")        AS kills,
       to_char(sum(lr."TotalValue"), 'FM999,999,999,999') AS total_gp
FROM "GameCharacters" gc
LEFT JOIN "LootRecords" lr ON lr."GameCharacterId" = gc."Id"
WHERE gc."RuneLiteId" LIKE 'seed-%'
GROUP BY gc."Id", gc."DisplayName"
ORDER BY gc."Id";

COMMIT;
