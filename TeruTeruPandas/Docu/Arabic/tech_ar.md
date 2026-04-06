# وثائق تقنية TeruTeruPandas

## البنية
TeruTeruPandas مكتبة C# تتكون من الوحدات التالية:
- **Core**: DataFrame, Series, Index, GroupBy, Join, Pivot, Melt
- **IO**: CSV, JSON, SQLite (قراءة/كتابة)
- **Compat**: مساعدات بأسلوب pandas
- **DataUniverse**: إدارة عدة DataFrame، واجهة SQL، إدخال/إخراج جماعي

## الفئات والواجهات الرئيسية
- `DataFrame`: كائن جدولي رئيسي يدعم الفهرسة، التصفية، التجميع، الدمج
- `Series<T>`: متجه أحادي البعد يدعم NA
- `Index`: الفئة الأساسية لـ RangeIndex, IntIndex, StringIndex, DateTimeIndex
- `BoolSeries`: أقنعة منطقية للتصفية
- `DataUniverse`: حاوية لعدة DataFrame، تدعم استعلامات SQL وIO جماعي

## محلل SQL والتنفيذ
- يدعم استعلامات SQL الأساسية: SELECT, WHERE, JOIN, GROUP BY, ORDER BY, LIMIT
- المحلل في DataUniverseSql.cs، التنفيذ عبر SqlQueryExecutor
- القيود: تعبيرات بسيطة فقط، لا يوجد استعلامات فرعية

## وحدات IO
- **CsvReader/CsvWriter**: قراءة وكتابة ملفات CSV
- **JsonIO**: قراءة وكتابة JSON (عادي وJSON Lines)
- **SqliteIO**: قراءة الجداول واستعلامات SQLite، تصدير DataFrame إلى جدول

## أمثلة API
```csharp
// قراءة CSV
var df = CsvReader.ReadCsv("data.csv");

// التجميع
var grouped = df.GroupBy("الفئة").Agg(new Dictionary<string, string[]> { ["القيمة"] = new[] { "sum", "mean" } });

// استعلام SQL
var universe = new DataUniverse();
universe.AddTable("جدول", df);
var result = universe.SqlExecute("SELECT * FROM جدول WHERE القيمة > 10");
```

## القيود والأداء
- مُحسّن لمجموعات البيانات الصغيرة والمتوسطة (حتى ~1 مليون صف)
- لا يدعم تعدد الخيوط أو الحوسبة الموزعة
- بعض العمليات (Pivot, Melt, Join المعقدة) قد تكون بطيئة مع البيانات الكبيرة

## التوسعة والمساهمة
- الكود قابل للتوسعة: يمكن إضافة وحدات IO جديدة، أنواع فهارس، طرق تجميع
- للتكامل مع قواعد بيانات خارجية استخدم SqliteIO أو نفذ وحدة IO خاصة بك
