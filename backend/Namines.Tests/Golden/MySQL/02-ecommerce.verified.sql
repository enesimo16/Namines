CREATE TABLE `Users` (
    `Id` INT NOT NULL AUTO_INCREMENT,
    `Email` VARCHAR(255) NOT NULL,
    `CreatedAt` DATETIME NOT NULL DEFAULT (UTC_TIMESTAMP())
    , PRIMARY KEY (`Id`)
) ENGINE=InnoDB;

CREATE TABLE `Products` (
    `Id` INT NOT NULL AUTO_INCREMENT,
    `Name` VARCHAR(200) NOT NULL,
    `Price` DECIMAL NOT NULL,
    `Stock` INT NOT NULL DEFAULT 0
    , PRIMARY KEY (`Id`)
) ENGINE=InnoDB;

CREATE TABLE `Orders` (
    `Id` INT NOT NULL AUTO_INCREMENT,
    `UserId` INT NOT NULL,
    `Total` DECIMAL NOT NULL,
    `PlacedAt` DATETIME NOT NULL
    , PRIMARY KEY (`Id`)
) ENGINE=InnoDB;

CREATE TABLE `OrderItems` (
    `Id` INT NOT NULL AUTO_INCREMENT,
    `OrderId` INT NOT NULL,
    `ProductId` INT NOT NULL,
    `Quantity` INT NOT NULL DEFAULT 1
    , PRIMARY KEY (`Id`)
) ENGINE=InnoDB;

ALTER TABLE `Orders` ADD CONSTRAINT `FK_Orders_Users_UserId` FOREIGN KEY(`UserId`)
REFERENCES `Users` (`Id`);

ALTER TABLE `OrderItems` ADD CONSTRAINT `FK_OrderItems_Orders_OrderId` FOREIGN KEY(`OrderId`)
REFERENCES `Orders` (`Id`);

ALTER TABLE `OrderItems` ADD CONSTRAINT `FK_OrderItems_Products_ProductId` FOREIGN KEY(`ProductId`)
REFERENCES `Products` (`Id`);

