CREATE TABLE IF NOT EXISTS `Orders` (
    `Id` INT NOT NULL AUTO_INCREMENT,
    PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `Products` (
    `Id` INT NOT NULL AUTO_INCREMENT,
    PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `OrderProducts` (
    `OrderId` INT NOT NULL,
    `ProductId` INT NOT NULL,
    `Quantity` INT NOT NULL,
    PRIMARY KEY (`OrderId`, `ProductId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

ALTER TABLE `OrderProducts` ADD CONSTRAINT `FK_OrderProducts_Orders_OrderId`
    FOREIGN KEY (`OrderId`) REFERENCES `Orders` (`Id`);

ALTER TABLE `OrderProducts` ADD CONSTRAINT `FK_OrderProducts_Products_ProductId`
    FOREIGN KEY (`ProductId`) REFERENCES `Products` (`Id`);

