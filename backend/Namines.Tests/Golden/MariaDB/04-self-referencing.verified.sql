CREATE TABLE IF NOT EXISTS `Categories` (
    `Id` INT NOT NULL AUTO_INCREMENT,
    `Name` VARCHAR(120) NOT NULL,
    `ParentId` INT NULL,
    PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

ALTER TABLE `Categories` ADD CONSTRAINT `FK_Categories_Categories_ParentId`
    FOREIGN KEY (`ParentId`) REFERENCES `Categories` (`Id`);

