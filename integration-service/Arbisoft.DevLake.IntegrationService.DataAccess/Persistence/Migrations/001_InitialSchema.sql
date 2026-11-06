CREATE TABLE EmailMappings (
    id BIGINT NOT NULL AUTO_INCREMENT,
    arbisoftEmail VARCHAR(320) NOT NULL,
    email VARCHAR(320) NOT NULL,
    createdOn DATETIME(6) NOT NULL,
    PRIMARY KEY (id),
    UNIQUE KEY UX_EmailMappings_arbisoftEmail_email (arbisoftEmail, email)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE Clients (
    id BIGINT NOT NULL AUTO_INCREMENT,
    clientId VARCHAR(128) COLLATE utf8mb4_bin NOT NULL,
    name VARCHAR(255) NOT NULL,
    isActive TINYINT(1) NOT NULL,
    accessTokenLifetimeSeconds INT NOT NULL,
    createdOn DATETIME(6) NOT NULL,
    createdBy VARCHAR(255) NOT NULL,
    modifiedOn DATETIME(6) NULL,
    modifiedBy VARCHAR(255) NULL,
    PRIMARY KEY (id),
    UNIQUE KEY UX_Clients_clientId (clientId)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE ClientSecrets (
    id BIGINT NOT NULL AUTO_INCREMENT,
    clientRefId BIGINT NOT NULL,
    secret VARCHAR(512) NOT NULL,
    expiresOn DATETIME(6) NOT NULL,
    revokedOn DATETIME(6) NULL,
    lastUsedOn DATETIME(6) NULL,
    createdOn DATETIME(6) NOT NULL,
    createdBy VARCHAR(255) NOT NULL,
    PRIMARY KEY (id),
    CONSTRAINT FK_ClientSecrets_Clients FOREIGN KEY (clientRefId) REFERENCES Clients (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE Scopes (
    id BIGINT NOT NULL AUTO_INCREMENT,
    name VARCHAR(128) NOT NULL,
    description VARCHAR(1024) NOT NULL,
    createdOn DATETIME(6) NOT NULL,
    PRIMARY KEY (id),
    UNIQUE KEY UX_Scopes_name (name)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE ClientScopes (
    id BIGINT NOT NULL AUTO_INCREMENT,
    clientRefId BIGINT NOT NULL,
    scopeId BIGINT NOT NULL,
    createdOn DATETIME(6) NOT NULL,
    createdBy VARCHAR(255) NOT NULL,
    PRIMARY KEY (id),
    UNIQUE KEY UX_ClientScopes_clientRefId_scopeId (clientRefId, scopeId),
    CONSTRAINT FK_ClientScopes_Clients FOREIGN KEY (clientRefId) REFERENCES Clients (id),
    CONSTRAINT FK_ClientScopes_Scopes FOREIGN KEY (scopeId) REFERENCES Scopes (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE SigningKeys (
    id BIGINT NOT NULL AUTO_INCREMENT,
    kid VARCHAR(128) COLLATE utf8mb4_bin NOT NULL,
    algorithm VARCHAR(32) NOT NULL,
    publicKey TEXT NOT NULL,
    privateKeyRef VARCHAR(255) NOT NULL,
    activatedOn DATETIME(6) NOT NULL,
    retiredOn DATETIME(6) NULL,
    createdOn DATETIME(6) NOT NULL,
    PRIMARY KEY (id),
    UNIQUE KEY UX_SigningKeys_kid (kid)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE AccessTokens (
    id BIGINT NOT NULL AUTO_INCREMENT,
    jti VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    clientRefId BIGINT NOT NULL,
    clientSecretId BIGINT NOT NULL,
    signingKeyId BIGINT NOT NULL,
    grantedScopes VARCHAR(2048) NOT NULL,
    issuedOn DATETIME(6) NOT NULL,
    expiresOn DATETIME(6) NOT NULL,
    revokedOn DATETIME(6) NULL,
    revokedReason VARCHAR(1024) NULL,
    issuedToIp VARCHAR(45) NOT NULL,
    PRIMARY KEY (id),
    UNIQUE KEY UX_AccessTokens_jti (jti),
    CONSTRAINT FK_AccessTokens_Clients FOREIGN KEY (clientRefId) REFERENCES Clients (id),
    CONSTRAINT FK_AccessTokens_ClientSecrets FOREIGN KEY (clientSecretId) REFERENCES ClientSecrets (id),
    CONSTRAINT FK_AccessTokens_SigningKeys FOREIGN KEY (signingKeyId) REFERENCES SigningKeys (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE RequestLogs (
    id BIGINT NOT NULL AUTO_INCREMENT,
    clientRefId BIGINT NOT NULL,
    correlationId VARCHAR(36) COLLATE utf8mb4_bin NOT NULL,
    url VARCHAR(2048) NOT NULL,
    status VARCHAR(64) NOT NULL,
    eventType VARCHAR(128) NOT NULL,
    createdOn DATETIME(6) NOT NULL,
    PRIMARY KEY (id),
    UNIQUE KEY UX_RequestLogs_correlationId (correlationId),
    KEY IX_RequestLogs_clientRefId_createdOn (clientRefId, createdOn),
    KEY IX_RequestLogs_status_createdOn (status, createdOn),
    CONSTRAINT FK_RequestLogs_Clients FOREIGN KEY (clientRefId) REFERENCES Clients (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE RequestLogJsons (
    id BIGINT NOT NULL AUTO_INCREMENT,
    requestLogId BIGINT NOT NULL,
    json JSON NOT NULL,
    PRIMARY KEY (id),
    CONSTRAINT FK_RequestLogJsons_RequestLogs FOREIGN KEY (requestLogId) REFERENCES RequestLogs (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE RequestResponseAttempts (
    id BIGINT NOT NULL AUTO_INCREMENT,
    requestLogId BIGINT NOT NULL,
    responseJson LONGTEXT NOT NULL,
    attemptStatus VARCHAR(64) NOT NULL,
    failureError TEXT NULL,
    attemptBy VARCHAR(255) NOT NULL,
    attemptOn DATETIME(6) NOT NULL,
    PRIMARY KEY (id),
    KEY IX_RequestResponseAttempts_requestLogId (requestLogId),
    CONSTRAINT FK_RequestResponseAttempts_RequestLogs FOREIGN KEY (requestLogId) REFERENCES RequestLogs (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE ClientConfigs (
    id BIGINT NOT NULL AUTO_INCREMENT,
    clientRefId BIGINT NOT NULL,
    authType VARCHAR(64) NOT NULL,
    baseUrl VARCHAR(2048) NOT NULL,
    tokenUrl VARCHAR(2048) NULL,
    outboundClientId VARCHAR(255) NULL,
    outboundClientSecretEncrypted TEXT NULL,
    outboundScope VARCHAR(1024) NULL,
    apiTokenEncrypted TEXT NULL,
    tokenExpiresOn DATETIME(6) NULL,
    tokenRotatedOn DATETIME(6) NULL,
    maxAttempts INT NOT NULL,
    isActive TINYINT(1) NOT NULL,
    createdOn DATETIME(6) NOT NULL,
    createdBy VARCHAR(255) NOT NULL,
    modifiedOn DATETIME(6) NULL,
    modifiedBy VARCHAR(255) NULL,
    PRIMARY KEY (id),
    UNIQUE KEY UX_ClientConfigs_clientRefId (clientRefId),
    CONSTRAINT FK_ClientConfigs_Clients FOREIGN KEY (clientRefId) REFERENCES Clients (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE ClientConfigOutboundUrls (
    id BIGINT NOT NULL AUTO_INCREMENT,
    clientConfigId BIGINT NOT NULL,
    eventType VARCHAR(128) NOT NULL,
    path VARCHAR(2048) NOT NULL,
    method VARCHAR(16) NOT NULL,
    isActive TINYINT(1) NOT NULL,
    createdOn DATETIME(6) NOT NULL,
    createdBy VARCHAR(255) NOT NULL,
    modifiedOn DATETIME(6) NULL,
    modifiedBy VARCHAR(255) NULL,
    PRIMARY KEY (id),
    KEY IX_ClientConfigOutboundUrls_clientConfigId_eventType (clientConfigId, eventType),
    CONSTRAINT FK_ClientConfigOutboundUrls_ClientConfigs FOREIGN KEY (clientConfigId) REFERENCES ClientConfigs (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
