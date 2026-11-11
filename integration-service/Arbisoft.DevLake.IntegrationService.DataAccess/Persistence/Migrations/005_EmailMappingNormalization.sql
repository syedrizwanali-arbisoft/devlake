-- Email mappings store the normalized address (trimmed, lowercase) in the columns that are matched, and the address as it was typed in a column of its own.
-- Both matched columns use a binary collation, so SQL compares exactly what the code compares: normalized text, byte for byte.
-- Rows written before this script held addresses that were already compared case-insensitively, so they are normalized in place.
-- To roll back, drop emailAsReceived and restore the default collation on arbisoftEmail and email.

ALTER TABLE EmailMappings
  MODIFY arbisoftEmail VARCHAR(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
  MODIFY email VARCHAR(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
  ADD COLUMN emailAsReceived VARCHAR(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NULL AFTER email;

UPDATE EmailMappings SET emailAsReceived = email;

UPDATE EmailMappings SET arbisoftEmail = LOWER(TRIM(arbisoftEmail)), email = LOWER(TRIM(email));

ALTER TABLE EmailMappings
  MODIFY emailAsReceived VARCHAR(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL;
