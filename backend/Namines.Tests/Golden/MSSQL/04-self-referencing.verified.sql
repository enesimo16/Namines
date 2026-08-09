CREATE TABLE [Categories] (
    [Id] INT IDENTITY(1,1) NOT NULL,
    [Name] NVARCHAR(120) NOT NULL,
    [ParentId] INT NULL
    , CONSTRAINT [PK_Categories] PRIMARY KEY CLUSTERED ([Id])
);

ALTER TABLE [Categories] WITH CHECK ADD CONSTRAINT [FK_Categories_Categories_ParentId] FOREIGN KEY([ParentId])
REFERENCES [Categories] ([Id]);

