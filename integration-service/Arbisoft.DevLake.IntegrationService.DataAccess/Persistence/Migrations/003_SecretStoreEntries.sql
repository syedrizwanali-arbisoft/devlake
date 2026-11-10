CREATE TABLE SecretStoreEntries (
    id BIGINT NOT NULL AUTO_INCREMENT,
    secretRef VARCHAR(255) COLLATE utf8mb4_bin NOT NULL,
    valueEncrypted TEXT NOT NULL,
    createdOn DATETIME(6) NOT NULL,
    createdBy VARCHAR(255) NOT NULL,
    PRIMARY KEY (id),
    UNIQUE KEY UX_SecretStoreEntries_secretRef (secretRef)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
