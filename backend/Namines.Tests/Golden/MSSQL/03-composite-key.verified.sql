CREATE TABLE [Orders] (
    [Id] INT IDENTITY(1,1) NOT NULL
    , CONSTRAINT [PK_Orders] PRIMARY KEY CLUSTERED ([Id])
);

CREATE TABLE [Products] (
    [Id] INT IDENTITY(1,1) NOT NULL
    , CONSTRAINT [PK_Products] PRIMARY KEY CLUSTERED ([Id])
);

CREATE TABLE [OrderProducts] (
    [OrderId] INT NOT NULL,
    [ProductId] INT NOT NULL,
    [Quantity] INT NOT NULL
    , CONSTRAINT [PK_OrderProducts] PRIMARY KEY CLUSTERED ([OrderId], [ProductId])
);

ALTER TABLE [OrderProducts] WITH CHECK ADD CONSTRAINT [FK_OrderProducts_Orders_OrderId] FOREIGN KEY([OrderId])
REFERENCES [Orders] ([Id]);

ALTER TABLE [OrderProducts] WITH CHECK ADD CONSTRAINT [FK_OrderProducts_Products_ProductId] FOREIGN KEY([ProductId])
REFERENCES [Products] ([Id]);

