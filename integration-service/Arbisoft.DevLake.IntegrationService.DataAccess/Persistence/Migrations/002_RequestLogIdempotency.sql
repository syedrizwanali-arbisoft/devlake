ALTER TABLE RequestLogs
    ADD COLUMN requestHash CHAR(64) NULL AFTER eventType,
    ADD KEY IX_RequestLogs_clientRefId_requestHash_createdOn (clientRefId, requestHash, createdOn);
