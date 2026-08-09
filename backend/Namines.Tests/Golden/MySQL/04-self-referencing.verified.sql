CREATE TABLE `Categories` (
    `Id` INT NOT NULL AUTO_INCREMENT,
    `Name` NVARCHAR(120) NOT NULL,
    `ParentId` INT NULL
    , PRIMARY KEY (`Id`)
) ENGINE=InnoDB;

ALTER TABLE `Categories` ADD CONSTRAINT `FK_Categories_Categories_ParentId` FOREIGN KEY(`ParentId`)
REFERENCES `Categories` (`Id`)
ON DELETE CASCADE;

