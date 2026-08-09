CREATE TABLE [Users] (
    [Id] INT IDENTITY(1,1) NOT NULL,
    [Email] NVARCHAR(255) NOT NULL,
    [CountryCode] CHAR(2) NOT NULL DEFAULT 'TR',
    [Age] INT NULL,
    [CreatedAt] DATETIME2 NOT NULL,
    [DeletedAt] DATETIME2 NULL
    , CONSTRAINT [PK_Users] PRIMARY KEY CLUSTERED ([Id])
    , CONSTRAINT [UQ_Users_Email] UNIQUE ([Email])
    , CONSTRAINT [CK_Users_Age] CHECK (Age IS NULL OR Age >= 0)
);

CREATE INDEX [IX_Users_CountryCode_CreatedAt] ON [Users] ([CountryCode], [CreatedAt] DESC);
CREATE UNIQUE INDEX [UX_Users_Email_Active] ON [Users] ([Email]) WHERE DeletedAt IS NULL;
CREATE INDEX [IX_Users_CreatedAt] ON [Users] ([CreatedAt]) INCLUDE ([Email]);

CREATE TABLE [Orders] (
    [Id] INT IDENTITY(1,1) NOT NULL,
    [UserId] INT NOT NULL,
    [Total] DECIMAL NOT NULL
    , CONSTRAINT [PK_Orders] PRIMARY KEY CLUSTERED ([Id])
    , CONSTRAINT [CK_Orders_ck2] CHECK (Total >= 0)
);

CREATE INDEX [IX_Orders_UserId] ON [Orders] ([UserId]);

ALTER TABLE [Orders] WITH CHECK ADD CONSTRAINT [FK_Orders_Users_UserId] FOREIGN KEY([UserId])
REFERENCES [Users] ([Id]);

