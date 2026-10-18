namespace ERP.Infrastructure.Import;

/// <summary>
/// The queries that read an EasyRetail database into the export document.
/// </summary>
/// <remarks>
/// <para>
/// One list, used twice: by the server when it can reach the database itself, and
/// written into the PowerShell script a shop runs on its own computer when it cannot.
/// Both produce the same document because both run these.
/// </para>
/// <para>
/// Every column is aliased to the property it fills and every value is made non-null in
/// the query. Windows PowerShell writes a database null as an empty object and a date as
/// a string it invents, so the queries hand it only text and numbers. Dates come out as
/// <c>yyyy-MM-dd</c> for the same reason.
/// </para>
/// <para>
/// Read against EasyRetail 2025 (database version 248.8). Balances are as at
/// <c>@AsAt</c>: each ledger's opening balance plus every posting in the
/// <c>Voucher</c> table up to the end of that day, debits positive.
/// </para>
/// </remarks>
internal static class EasyRetailQueries
{
    /// <summary>The sections of the document, in the order they are written.</summary>
    public static IReadOnlyList<(string Name, bool Single, string Sql)> Sections { get; } =
    [
        ("company", true, """
            SELECT TOP 1
                ISNULL(Name, '') AS name,
                LTRIM(RTRIM(ISNULL(Address1, '') + ' ' + ISNULL(Address2, '') + ' ' + ISNULL(Address3, ''))) AS address,
                ISNULL(City, '') AS city,
                LTRIM(RTRIM(ISNULL(Phone, ''))) AS phone,
                LTRIM(RTRIM(ISNULL(Mobile, ''))) AS mobile,
                ISNULL(Email, '') AS email,
                ISNULL(TIN, '') AS taxNumber,
                CONVERT(varchar(10), BookBegin, 23) AS bookBegin
            FROM Company ORDER BY ID
            """),
        ("groups", false, """
            SELECT ID AS id, ISNULL(Name, '') AS name, ISNULL(UnderID, 0) AS underId
            FROM Schedule ORDER BY ID
            """),
        ("ledgers", false, """
            SELECT
                l.ID AS id,
                LTRIM(RTRIM(REPLACE(ISNULL(l.Name, ''), CHAR(9), ' '))) AS name,
                l.ScheduleID AS groupId,
                CAST(CASE WHEN l.IsDebit = 1 THEN l.Amount ELSE -l.Amount END AS decimal(19, 4)) AS opening,
                CAST(CASE WHEN l.IsDebit = 1 THEN l.Amount ELSE -l.Amount END + ISNULL(m.net, 0) AS decimal(19, 4)) AS balance,
                ISNULL(m.postings, 0) AS postings
            FROM Ledger l
            LEFT JOIN (
                SELECT LedgerID,
                    SUM(CASE WHEN IsDebit = 1 THEN Amount ELSE -Amount END) AS net,
                    COUNT(*) AS postings
                FROM Voucher
                WHERE Date < DATEADD(day, 1, @AsAt)
                GROUP BY LedgerID
            ) m ON m.LedgerID = l.ID
            ORDER BY l.ID
            """),
        ("customers", false, """
            SELECT
                c.ID AS id, c.LedgerID AS ledgerId, '' AS code, ISNULL(c.Name, '') AS name,
                LTRIM(RTRIM(ISNULL(c.Address1, '') + ' ' + ISNULL(c.Address2, ''))) AS address1,
                LTRIM(RTRIM(ISNULL(c.Address3, ''))) AS address2,
                ISNULL(c.Mobile, '') AS mobile, ISNULL(c.Phone, '') AS phone, ISNULL(c.EMail, '') AS email,
                COALESCE(NULLIF(LTRIM(RTRIM(c.TIN)), ''), NULLIF(LTRIM(RTRIM(c.GSTID)), ''), '') AS taxNumber,
                CAST(ISNULL(c.CreditLimit, 0) AS decimal(19, 4)) AS creditLimit,
                ISNULL(c.CreditPeriod, 0) AS creditDays,
                LTRIM(RTRIM(ISNULL(c.City, ''))) AS city,
                LTRIM(RTRIM(ISNULL(c.State, ''))) AS state,
                LTRIM(RTRIM(ISNULL(c.Country, ''))) AS country,
                LTRIM(RTRIM(ISNULL(c.PINNo, ''))) AS pinCode,
                LTRIM(RTRIM(ISNULL(c.Website, ''))) AS website,
                LTRIM(RTRIM(ISNULL(c.Fax, ''))) AS fax,
                LTRIM(RTRIM(ISNULL(c.ContactPerson, ''))) AS contactPerson,
                LTRIM(RTRIM(ISNULL(c.TIN, ''))) AS tin,
                LTRIM(RTRIM(ISNULL(c.GSTID, ''))) AS gstId,
                LTRIM(RTRIM(ISNULL(c.CSTRegNo, ''))) AS cstNumber,
                LTRIM(RTRIM(ISNULL(a.Name, ''))) AS area,
                LTRIM(RTRIM(ISNULL(r.Name, ''))) AS route,
                CASE WHEN c.IsSMS = 1 THEN 1 ELSE 0 END AS isSms
            FROM Customer c
            LEFT JOIN Area a ON a.ID = c.AreaID
            LEFT JOIN Route r ON r.ID = c.RouteID
            ORDER BY c.ID
            """),
        ("suppliers", false, """
            SELECT
                s.ID AS id, s.LedgerID AS ledgerId, LTRIM(RTRIM(ISNULL(s.Code, ''))) AS code, ISNULL(s.Name, '') AS name,
                LTRIM(RTRIM(ISNULL(s.Address1, '') + ' ' + ISNULL(s.Address2, ''))) AS address1,
                LTRIM(RTRIM(ISNULL(s.Address3, ''))) AS address2,
                ISNULL(s.Mobile, '') AS mobile, ISNULL(s.Phone, '') AS phone, ISNULL(s.EMail, '') AS email,
                ISNULL(LTRIM(RTRIM(s.TIN)), '') AS taxNumber,
                CAST(ISNULL(s.CreditLimit, 0) AS decimal(19, 4)) AS creditLimit,
                ISNULL(s.CreditPeriod, 0) AS creditDays,
                LTRIM(RTRIM(ISNULL(s.City, ''))) AS city,
                LTRIM(RTRIM(ISNULL(s.State, ''))) AS state,
                LTRIM(RTRIM(ISNULL(n.Name, ''))) AS country,
                LTRIM(RTRIM(ISNULL(s.PINNo, ''))) AS pinCode,
                LTRIM(RTRIM(ISNULL(s.Website, ''))) AS website,
                LTRIM(RTRIM(ISNULL(s.Fax, ''))) AS fax,
                LTRIM(RTRIM(ISNULL(s.ContactPerson, ''))) AS contactPerson,
                LTRIM(RTRIM(ISNULL(s.TIN, ''))) AS tin,
                '' AS gstId,
                LTRIM(RTRIM(ISNULL(s.CSTRegNo, ''))) AS cstNumber,
                '' AS area,
                '' AS route,
                CASE WHEN s.IsSMS = 1 THEN 1 ELSE 0 END AS isSms
            FROM Supplier s
            LEFT JOIN Country n ON n.ID = s.CountryID
            ORDER BY s.ID
            """),
        ("employees", false, """
            SELECT ID AS id, LedgerID AS ledgerId, ISNULL(Name, '') AS name,
                ISNULL(Mobile, '') AS mobile, ISNULL(EMail, '') AS email
            FROM Employee ORDER BY ID
            """),
        ("categories", false, """
            SELECT ID AS id, ISNULL(Name, '') AS name, ISNULL(UnderID, 0) AS underId
            FROM Category ORDER BY ID
            """),
        ("subClasses", false, """
            SELECT ID AS id, ISNULL(Name, '') AS name FROM SubClass ORDER BY ID
            """),
        ("brands", false, """
            SELECT ID AS id, ISNULL(Name, '') AS name FROM Brand ORDER BY ID
            """),
        ("units", false, """
            SELECT ID AS id, ISNULL(Name, '') AS name, ISNULL(ShortName, '') AS shortName,
                CASE WHEN BaseMeasurementID IS NULL OR BaseMeasurementID = ID THEN 0 ELSE BaseMeasurementID END AS baseId,
                CAST(CASE WHEN ISNULL(Quantity, 0) = 0 THEN 1 ELSE ISNULL(BaseQuantity, Quantity) / Quantity END AS decimal(19, 6)) AS factor
            FROM Measurement ORDER BY ID
            """),
        ("warehouses", false, """
            SELECT ID AS id, ISNULL(Name, '') AS name FROM Godown ORDER BY ID
            """),
        ("products", false, """
            SELECT
                p.ID AS id, LTRIM(RTRIM(ISNULL(p.Code, ''))) AS code, LTRIM(RTRIM(ISNULL(p.Name, ''))) AS name,
                LTRIM(RTRIM(ISNULL(p.Arabic, ''))) AS nameArabic,
                p.CategoryID AS categoryId, p.SubClassID AS subClassId, p.BrandID AS brandId,
                p.MeasurementID AS unitId, p.PurchaseUOMID AS purchaseUnitId, p.SalesUOMID AS salesUnitId,
                p.ItemTypeID AS itemType,
                CAST(p.PurchaseRate AS decimal(19, 4)) AS cost,
                CAST(p.RetailSalesRate AS decimal(19, 4)) AS retail,
                CAST(p.WholeSaleRate AS decimal(19, 4)) AS wholesale,
                CAST(p.OtherSalesRate AS decimal(19, 4)) AS other,
                CAST(p.MRP AS decimal(19, 4)) AS mrp,
                CAST(p.ProfitPercentage AS decimal(19, 4)) AS profitPercent,
                CAST(p.MinOrderLevel AS decimal(19, 4)) AS minLevel,
                CAST(p.ReOrderLevel AS decimal(19, 4)) AS reorderLevel,
                CAST(p.MaxOrderLevel AS decimal(19, 4)) AS maxLevel,
                ISNULL(p.Rack, '') AS rack, ISNULL(p.Bin, '') AS bin,
                LTRIM(RTRIM(ISNULL(p.UPC, ''))) AS upc, LTRIM(RTRIM(ISNULL(p.EANCode, ''))) AS ean,
                ISNULL(p.ShortDescription, '') AS shortDescription, ISNULL(p.Manufacture, '') AS manufacturer,
                ISNULL(p.Size, '') AS size, ISNULL(o.Name, '') AS origin,
                ISNULL(d.Name, '') AS device, ISNULL(co.Name, '') AS colour, ISNULL(ba.Name, '') AS battery,
                ISNULL(r.Name, '') AS ram, ISNULL(st.Name, '') AS storage,
                CAST(CASE WHEN ISNULL(p.IsDeactive, 0) = 1 THEN 0 ELSE 1 END AS bit) AS isActive
            FROM Product p
            LEFT JOIN Origin o ON o.ID = p.OriginID
            LEFT JOIN Device d ON d.ID = p.DeviceID
            LEFT JOIN Color co ON co.ID = p.ColorID
            LEFT JOIN Battery ba ON ba.ID = p.BatteryID
            LEFT JOIN Ram r ON r.ID = p.RamID
            LEFT JOIN Storage st ON st.ID = p.StorageID
            ORDER BY p.ID
            """),
        ("barcodes", false, """
            SELECT ProductID AS productId, LTRIM(RTRIM(ISNULL(UPC, ''))) AS barcode,
                CAST(ISNULL(Cost, 0) AS decimal(19, 4)) AS cost,
                CAST(ISNULL(Retail, 0) AS decimal(19, 4)) AS retail,
                CAST(ISNULL(WholeSale, 0) AS decimal(19, 4)) AS wholesale,
                CAST(ISNULL(MRP, 0) AS decimal(19, 4)) AS mrp
            FROM BarCode ORDER BY ID
            """),
        ("stock", false, """
            SELECT s.ProductID AS productId, s.GodownID AS warehouseId, ISNULL(s.Batch, '') AS batch,
                CAST(s.Quantity AS decimal(19, 4)) AS quantity,
                CAST(ISNULL(p.PurchaseRate, 0) AS decimal(19, 4)) AS cost
            FROM Stock s JOIN Product p ON p.ID = s.ProductID
            WHERE s.Quantity <> 0
            ORDER BY s.ID
            """),
    ];
}
