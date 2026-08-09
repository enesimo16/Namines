CREATE TABLE `Users` (
    `Id` INT NOT NULL AUTO_INCREMENT,
    `Email` NVARCHAR(255) NOT NULL,
    `CountryCode` CHAR(2) NOT NULL DEFAULT 'TR',
    `Age` INT NULL,
    `CreatedAt` DATETIME2 NOT NULL,
    `DeletedAt` DATETIME2 NULL
    , PRIMARY KEY (`Id`)
    , CONSTRAINT `UQ_Users_Email` UNIQUE (`Email`)
    , CONSTRAINT `CK_Users_Age` CHECK (Age IS NULL OR Age >= 0)
) ENGINE=InnoDB;

CREATE INDEX `IX_Users_CountryCode_CreatedAt` ON `Users` (`CountryCode`, `CreatedAt` DESC);
CREATE UNIQUE INDEX `UX_Users_Email_Active` ON `Users` (`Email`) /* WHERE DeletedAt IS NULL — MySQL kısmi index desteklemiyor */;
CREATE INDEX `IX_Users_CreatedAt` ON `Users` (`CreatedAt`) /* INCLUDE (Email) — MySQL desteklemiyor */;

CREATE TABLE `Orders` (
    `Id` INT NOT NULL AUTO_INCREMENT,
    `UserId` INT NOT NULL,
    `Total` DECIMAL NOT NULL
    , PRIMARY KEY (`Id`)
    , CONSTRAINT `CK_Orders_ck2` CHECK (Total >= 0)
) ENGINE=InnoDB;

CREATE INDEX `IX_Orders_UserId` ON `Orders` (`UserId`);

ALTER TABLE `Orders` ADD CONSTRAINT `FK_Orders_Users_UserId` FOREIGN KEY(`UserId`)
REFERENCES `Users` (`Id`);

