CREATE TABLE `Users` (
    `Id` INT NOT NULL AUTO_INCREMENT,
    `Email` VARCHAR(255) NOT NULL
    , PRIMARY KEY (`Id`)
) ENGINE=InnoDB;

CREATE TABLE `Addresses` (
    `Id` INT NOT NULL AUTO_INCREMENT,
    `UserId` INT NOT NULL,
    `Line1` VARCHAR(200) NOT NULL
    , PRIMARY KEY (`Id`)
) ENGINE=InnoDB;

CREATE TABLE `Orders` (
    `Id` INT NOT NULL AUTO_INCREMENT,
    `UserId` INT NOT NULL,
    `AddressId` INT NOT NULL
    , PRIMARY KEY (`Id`)
) ENGINE=InnoDB;

ALTER TABLE `Addresses` ADD CONSTRAINT `FK_Addresses_Users_UserId` FOREIGN KEY(`UserId`)
REFERENCES `Users` (`Id`);

ALTER TABLE `Orders` ADD CONSTRAINT `FK_Orders_Users_UserId` FOREIGN KEY(`UserId`)
REFERENCES `Users` (`Id`);

ALTER TABLE `Orders` ADD CONSTRAINT `FK_Orders_Addresses_AddressId` FOREIGN KEY(`AddressId`)
REFERENCES `Addresses` (`Id`);

