CREATE TABLE "LicenseConfigurations" (
    "HardwareId" TEXT NOT NULL CONSTRAINT "PK_LicenseConfigurations" PRIMARY KEY,
    "Tier" TEXT NOT NULL,
    "ExpiresAtUtc" TEXT NOT NULL,
    "MaxKiosks" INTEGER NOT NULL
);


CREATE TABLE "Products" (
    "Ean13" TEXT NOT NULL CONSTRAINT "PK_Products" PRIMARY KEY,
    "Description" TEXT NOT NULL,
    "UsdPrice" TEXT NOT NULL,
    "KhrPrice" TEXT NOT NULL
);


CREATE TABLE "Transactions" (
    "TransactionGuid" TEXT NOT NULL CONSTRAINT "PK_Transactions" PRIMARY KEY,
    "CreatedAtUtc" TEXT NOT NULL,
    "TotalUsd" TEXT NOT NULL,
    "TenderedUsd" TEXT NOT NULL,
    "PaymentMethod" INTEGER NULL,
    "SyncStatus" TEXT NOT NULL
);


CREATE TABLE "LineItem" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_LineItem" PRIMARY KEY AUTOINCREMENT,
    "Ean13" TEXT NOT NULL,
    "Description" TEXT NOT NULL,
    "UnitPriceUsd" TEXT NOT NULL,
    "Quantity" INTEGER NOT NULL,
    "TransactionGuid" TEXT NOT NULL,
    CONSTRAINT "FK_LineItem_Transactions_TransactionGuid" FOREIGN KEY ("TransactionGuid") REFERENCES "Transactions" ("TransactionGuid") ON DELETE CASCADE
);


CREATE INDEX "IX_LineItem_TransactionGuid" ON "LineItem" ("TransactionGuid");


