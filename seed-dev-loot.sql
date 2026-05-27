-- ============================================================================
-- seed-dev-loot.sql  —  Local dev seed data for the character drop-log dashboard
-- ============================================================================
--
-- Generates several GameCharacters and a few thousand realistic LootRecords
-- (kills + JSONB drop tables) so the /loot/log/{id} dashboard, source cards,
-- dry streaks, first-time highlights and feed tiers all have data to render.
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
--     psql "postgresql://postgres:postgres@localhost:5432/klavlor" -f seed-dev-loot.sql
--
-- TUNING VOLUME
--   Bump the `kills` numbers in the seed_source VALUES list below (or wrap them
--   in a multiplier) to generate more/less data. Defaults to ~2,800 records.
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
-- 3. Insert the seed characters (owned by the resolved user, visible to all).
-- ----------------------------------------------------------------------------
INSERT INTO "GameCharacters"
    ("UserId", "RuneLiteId", "DisplayName", "IsVisible", "IsAdminHidden", "SavedAt", "SavedById")
SELECT s.user_id, v.rl, v.dn, true, false, now(), s.user_id
FROM seed_ctx s
CROSS JOIN (VALUES
    ('seed-ironfodder', 'Iron Fodder'),
    ('seed-zukkenmir',  'Zukkenmir'),
    ('seed-claudelock',  'ClaudeLock')
) AS v(rl, dn);

-- ----------------------------------------------------------------------------
-- 4. Source definitions: (name, LootSourceType, combat level, kills to generate)
--    combat 0 -> stored as NULL (raids/events have no combat level).
-- ----------------------------------------------------------------------------
CREATE TEMP TABLE seed_source (source text, stype text, combat int, kills int) ON COMMIT DROP;
INSERT INTO seed_source VALUES
    ('Vorkath',           'Npc',         732,  90),
    ('Zulrah',            'Npc',         725, 110),
    ('Alchemical Hydra',  'Npc',         426,  80),
    ('Cerberus',          'Npc',         318,  90),
    ('General Graardor',  'Npc',         624,  70),
    ('Kree''arra',        'Npc',         580,  60),
    ('Corporeal Beast',   'Npc',         785,  50),
    ('Nex',               'Npc',        1001,  55),
    ('Vardorvis',         'Npc',         784,  65),
    ('Chambers of Xeric', 'Event',         0,  40),
    ('Theatre of Blood',  'Event',         0,  35),
    ('Tombs of Amascut',  'Event',         0,  45),
    ('Master Farmer',     'Pickpocket',   38, 120),
    ('PvP Kill',          'Player',      126,  30);

-- ----------------------------------------------------------------------------
-- 5. Drop tables: (source, item name, OSRS item id, unit GE price, max qty, drop chance)
--    Every source has at least one prob=1.0 "guaranteed" common so no kill is empty.
--    Prices are approximate; rare-drop probabilities are inflated vs. real rates
--    so the dashboard has interesting drops at modest volume.
-- ----------------------------------------------------------------------------
CREATE TEMP TABLE seed_drop (source text, name text, item_id int, unit_price bigint, max_qty int, prob double precision) ON COMMIT DROP;
INSERT INTO seed_drop VALUES
    -- Vorkath
    ('Vorkath', 'Superior dragon bones', 22124,     8000, 30, 1.00),
    ('Vorkath', 'Blue dragonhide',        1751,     1600, 30, 1.00),
    ('Vorkath', 'Dragonbone necklace',   22095,    90000,  1, 0.020),
    ('Vorkath', 'Jar of decay',          22106,   120000,  1, 0.010),
    ('Vorkath', 'Skeletal visage',       22006, 55000000,  1, 0.020),
    ('Vorkath', 'Vorki',                 21992,        0,  1, 0.005),
    -- Zulrah
    ('Zulrah', 'Zulrah''s scales',       12934,      250, 1000, 1.00),
    ('Zulrah', 'Snakeskin',               6289,      200,   50, 0.80),
    ('Zulrah', 'Tanzanite fang',         12922,  3200000,    1, 0.030),
    ('Zulrah', 'Magic fang',             12932,  2800000,    1, 0.030),
    ('Zulrah', 'Serpentine visage',      12927,  6500000,    1, 0.020),
    ('Zulrah', 'Jar of swamp',           12936,   120000,    1, 0.010),
    ('Zulrah', 'Pet snakeling',          12921,        0,    1, 0.004),
    -- Alchemical Hydra
    ('Alchemical Hydra', 'Hydra leather', 22983,  110000,   1, 1.00),
    ('Alchemical Hydra', 'Dragon thrownaxe', 21205,  100, 200, 0.70),
    ('Alchemical Hydra', 'Hydra''s claw', 22966, 1200000,   1, 0.030),
    ('Alchemical Hydra', 'Hydra tail',    22988,   90000,   1, 0.050),
    ('Alchemical Hydra', 'Brimstone ring', 22975, 800000,   1, 0.020),
    ('Alchemical Hydra', 'Ikkle hydra',   22746,       0,   1, 0.004),
    -- Cerberus
    ('Cerberus', 'Infernal ashes',       25775,    1500,  5, 1.00),
    ('Cerberus', 'Key master teleport',  13249,    2000,  3, 0.50),
    ('Cerberus', 'Primordial crystal',   13231, 3000000,  1, 0.030),
    ('Cerberus', 'Pegasian crystal',     13229, 2500000,  1, 0.030),
    ('Cerberus', 'Eternal crystal',      13227, 2800000,  1, 0.030),
    ('Cerberus', 'Smouldering stone',    13233,  450000,  1, 0.020),
    ('Cerberus', 'Hellpuppy',            13247,       0,  1, 0.004),
    -- General Graardor
    ('General Graardor', 'Coins',           995,        1, 50000, 1.00),
    ('General Graardor', 'Bandos boots',  11836,   250000,     1, 0.060),
    ('General Graardor', 'Bandos chestplate', 11832, 17000000, 1, 0.030),
    ('General Graardor', 'Bandos tassets', 11834, 22000000,    1, 0.030),
    ('General Graardor', 'Bandos hilt',   11812,  8000000,     1, 0.020),
    ('General Graardor', 'Pet general graardor', 12650, 0,     1, 0.003),
    -- Kree'arra
    ('Kree''arra', 'Coins',                 995,        1, 40000, 1.00),
    ('Kree''arra', 'Armadyl helmet',      11826,  1200000,     1, 0.030),
    ('Kree''arra', 'Armadyl chestplate',  11828, 12000000,     1, 0.030),
    ('Kree''arra', 'Armadyl chainskirt',  11830,  9000000,     1, 0.030),
    ('Kree''arra', 'Armadyl hilt',        11810,  4000000,     1, 0.020),
    ('Kree''arra', 'Pet kree''arra',      12649,        0,     1, 0.003),
    -- Corporeal Beast
    ('Corporeal Beast', 'Spirit shield',  12829,    35000, 1, 1.00),
    ('Corporeal Beast', 'Holy elixir',    12833,   350000, 1, 0.050),
    ('Corporeal Beast', 'Spectral sigil', 12821, 60000000, 1, 0.010),
    ('Corporeal Beast', 'Arcane sigil',   12825, 18000000, 1, 0.010),
    ('Corporeal Beast', 'Elysian sigil',  12819, 700000000,1, 0.008),
    ('Corporeal Beast', 'Pet dark core',  13182,        0, 1, 0.003),
    -- Nex
    ('Nex', 'Nihil shard',               26211,     4000, 30, 1.00),
    ('Nex', 'Torva full helm',           26382, 38000000,  1, 0.020),
    ('Nex', 'Torva platebody',           26384, 50000000,  1, 0.020),
    ('Nex', 'Torva platelegs',           26386, 45000000,  1, 0.020),
    ('Nex', 'Nihil horn',                26372, 14000000,  1, 0.020),
    ('Nex', 'Zaryte vambraces',          26235,  4500000,  1, 0.030),
    ('Nex', 'Ancient hilt',              26370, 90000000,  1, 0.010),
    ('Nex', 'Nexling',                   26348,        0,  1, 0.002),
    -- Vardorvis
    ('Vardorvis', 'Coins',                  995,        1, 30000, 1.00),
    ('Vardorvis', 'Awakener''s orb',      28334,  1500000,     2, 0.050),
    ('Vardorvis', 'Virtus mask',          26241, 25000000,     1, 0.020),
    ('Vardorvis', 'Virtus robe top',      26243, 30000000,     1, 0.020),
    ('Vardorvis', 'Virtus robe bottom',   26245, 28000000,     1, 0.020),
    ('Vardorvis', 'Ultor vestige',        28285, 60000000,     1, 0.020),
    ('Vardorvis', 'Butch',                28250,        0,     1, 0.002),
    -- Chambers of Xeric (raid)
    ('Chambers of Xeric', 'Coins',           995,          1, 200000, 1.00),
    ('Chambers of Xeric', 'Twisted buckler', 21000,   4000000,     1, 0.040),
    ('Chambers of Xeric', 'Ancestral hat',   21018,  28000000,     1, 0.020),
    ('Chambers of Xeric', 'Dragon claws',    13652,  75000000,     1, 0.020),
    ('Chambers of Xeric', 'Elder maul',      21003,  70000000,     1, 0.020),
    ('Chambers of Xeric', 'Kodai insignia',  21043,  80000000,     1, 0.020),
    ('Chambers of Xeric', 'Twisted bow',     20997,1200000000,     1, 0.010),
    ('Chambers of Xeric', 'Olmlet',          21291,         0,     1, 0.002),
    -- Theatre of Blood (raid)
    ('Theatre of Blood', 'Coins',                995,         1, 150000, 1.00),
    ('Theatre of Blood', 'Justiciar faceguard',22326,   8000000,     1, 0.030),
    ('Theatre of Blood', 'Ghrazi rapier',      22324,  90000000,     1, 0.020),
    ('Theatre of Blood', 'Sanguinesti staff (uncharged)', 22481, 75000000, 1, 0.020),
    ('Theatre of Blood', 'Avernic defender hilt', 22477, 70000000,   1, 0.020),
    ('Theatre of Blood', 'Scythe of vitur (uncharged)', 22486, 750000000, 1, 0.010),
    ('Theatre of Blood', 'Lil'' zik',          22473,         0,     1, 0.002),
    -- Tombs of Amascut (raid)
    ('Tombs of Amascut', 'Coins',                995,          1, 120000, 1.00),
    ('Tombs of Amascut', 'Lightbearer',        27202,    4000000,     1, 0.040),
    ('Tombs of Amascut', 'Osmumten''s fang',   26219,   25000000,     1, 0.030),
    ('Tombs of Amascut', 'Elidinis'' ward',    27251,   30000000,     1, 0.020),
    ('Tombs of Amascut', 'Masori body (f)',    27229,   60000000,     1, 0.020),
    ('Tombs of Amascut', 'Tumeken''s shadow (uncharged)', 27277, 1000000000, 1, 0.010),
    ('Tombs of Amascut', 'Tumeken''s guardian',27352,          0,     1, 0.002),
    -- Master Farmer (pickpocket)
    ('Master Farmer', 'Potato seed',      5318,     5, 5, 1.00),
    ('Master Farmer', 'Ranarr seed',      5295, 35000, 1, 0.100),
    ('Master Farmer', 'Snapdragon seed',  5300, 50000, 1, 0.050),
    ('Master Farmer', 'Torstol seed',     5304, 60000, 1, 0.030),
    ('Master Farmer', 'Magic seed',       5316, 90000, 1, 0.020),
    -- PvP Kill (player)
    ('PvP Kill', 'Coins',          995,      1, 100000, 1.00),
    ('PvP Kill', 'Looting bag',  11941,   5000,     1, 0.30),
    ('PvP Kill', 'Dragon dagger',  1215,  17000,     1, 0.20);

-- ----------------------------------------------------------------------------
-- 6. Generate one row per (character, source, kill) with a random timestamp
--    over the last 150 days.
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
CROSS JOIN seed_source s
CROSS JOIN LATERAL generate_series(1, s.kills) AS g(n)
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
-- 8. Insert into LootRecords. KillCount is monotonic with time per (char,source)
--    plus a per-source base offset so counts look lived-in.
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
    (row_number() OVER (PARTITION BY character_id, source ORDER BY occurred_at)
        + (abs(hashtext(source)) % 500))::int AS kill_count,
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
