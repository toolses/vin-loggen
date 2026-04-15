-- 022_NormalizeEnglishWineTypes.sql
-- Fix wine types that were saved in English before the type-mapping logic was added.

UPDATE wines SET type = 'Hvit'        WHERE lower(type) = 'white';
UPDATE wines SET type = 'Rød'         WHERE lower(type) = 'red';
UPDATE wines SET type = 'Rosé'        WHERE lower(type) IN ('rosé', 'rose');
UPDATE wines SET type = 'Musserende'  WHERE lower(type) = 'sparkling';
UPDATE wines SET type = 'Oransje'     WHERE lower(type) = 'orange';
UPDATE wines SET type = 'Dessert'     WHERE lower(type) = 'dessert';
