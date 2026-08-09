CREATE TABLE `Orders` (
    `Id` INT NOT NULL AUTO_INCREMENT
    , PRIMARY KEY (`Id`)
) ENGINE=InnoDB;

CREATE TABLE `Products` (
    `Id` INT NOT NULL AUTO_INCREMENT
    , PRIMARY KEY (`Id`)
) ENGINE=InnoDB;

CREATE TABLE `OrderProducts` (
    `OrderId` INT NOT NULL,
    `ProductId` INT NOT NULL,
    `Quantity` INT NOT NULL
    , PRIMARY KEY (`OrderId`, `ProductId`)
) ENGINE=InnoDB;

ALTER TABLE `OrderProducts` ADD CONSTRAINT `FK_OrderProducts_Orders_OrderId` FOREIGN KEY(`OrderId`)
REFERENCES `Orders` (`Id`);

ALTER TABLE `OrderProducts` ADD CONSTRAINT `FK_OrderProducts_Products_ProductId` FOREIGN KEY(`ProductId`)
REFERENCES `Products` (`Id`);

