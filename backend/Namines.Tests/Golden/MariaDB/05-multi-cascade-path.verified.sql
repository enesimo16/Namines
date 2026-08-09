CREATE TABLE IF NOT EXISTS `Users` (
    `Id` INT NOT NULL AUTO_INCREMENT,
    `Email` NVARCHAR(255) NOT NULL,
    PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `Addresses` (
    `Id` INT NOT NULL AUTO_INCREMENT,
    `UserId` INT NOT NULL,
    `Line1` NVARCHAR(200) NOT NULL,
    PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `Orders` (
    `Id` INT NOT NULL AUTO_INCREMENT,
    `UserId` INT NOT NULL,
    `AddressId` INT NOT NULL,
    PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

ALTER TABLE `Addresses` ADD CONSTRAINT `FK_Addresses_Users_UserId`
    FOREIGN KEY (`UserId`) REFERENCES `Users` (`Id`);

ALTER TABLE `Orders` ADD CONSTRAINT `FK_Orders_Users_UserId`
    FOREIGN KEY (`UserId`) REFERENCES `Users` (`Id`);

ALTER TABLE `Orders` ADD CONSTRAINT `FK_Orders_Addresses_AddressId`
    FOREIGN KEY (`AddressId`) REFERENCES `Addresses` (`Id`);

