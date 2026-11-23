-- دیدن ردیف‌های تکراری
SELECT RowNumber, COUNT(*) AS Cnt
FROM FruitRecords
GROUP BY RowNumber
HAVING COUNT(*) > 1
ORDER BY RowNumber;

-- اصلاح: به ردیف‌های تکراری، شماره‌های جدید بده (از بزرگ‌ترین + 1)
WITH Duplicates AS (
    SELECT Id, RowNumber,
           ROW_NUMBER() OVER (PARTITION BY RowNumber ORDER BY Id) AS rn
    FROM FruitRecords
)
UPDATE f
SET f.RowNumber = (SELECT MAX(RowNumber) FROM FruitRecords) + d.rn
FROM FruitRecords f
JOIN Duplicates d ON f.Id = d.Id
WHERE d.rn > 1;