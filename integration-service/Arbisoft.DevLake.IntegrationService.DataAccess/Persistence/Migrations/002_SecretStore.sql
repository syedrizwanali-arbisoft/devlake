-- Encrypted secret store. privateKeyRef in SigningKeys names a row here.
-- encryptedValue is AES-256-GCM output produced by the service; the master key never touches the database.
-- This script is purely additive. To roll it back, drop SecretStoreEntries.

CREATE TABLE SecretStoreEntries (
  id BIGINT NOT NULL AUTO_INCREMENT,
  secretRef VARCHAR(255) NOT NULL,
  encryptedValue TEXT NOT NULL,
  createdOn DATETIME(6) NOT NULL,
  modifiedOn DATETIME(6) NULL,
  PRIMARY KEY (id),
  UNIQUE KEY UX_SecretStoreEntries_secretRef (secretRef)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
