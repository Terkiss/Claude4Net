# TeruTeruPandas तकनीकी दस्तावेज़

## आर्किटेक्चर
TeruTeruPandas C# में निर्मित है और इसमें ये मुख्य मॉड्यूल हैं:
- **Core**: DataFrame, Series, Index, GroupBy, Join, Pivot, Melt
- **IO**: CSV, JSON, SQLite (पढ़ना/लिखना)
- **Compat**: pandas शैली के हेल्पर
- **DataUniverse**: कई DataFrame का प्रबंधन, SQL इंटरफ़ेस, मास IO

## मुख्य क्लास और इंटरफेस
- `DataFrame`: मुख्य टेबल ऑब्जेक्ट, इंडेक्सिंग, फ़िल्टरिंग, एग्रीगेशन, मर्जिंग सपोर्ट करता है
- `Series<T>`: NA सपोर्ट के साथ 1D वेक्टर
- `Index`: RangeIndex, IntIndex, StringIndex, DateTimeIndex के लिए बेस क्लास
- `BoolSeries`: फ़िल्टरिंग के लिए बूलियन मास्क
- `DataUniverse`: कई DataFrame के लिए कंटेनर, SQL क्वेरी और मास IO सपोर्ट करता है

## SQL पार्सर और निष्पादन
- बेसिक SQL क्वेरी (SELECT, WHERE, JOIN, GROUP BY, ORDER BY, LIMIT) सपोर्ट करता है
- पार्सर DataUniverseSql.cs में, निष्पादन SqlQueryExecutor से
- सीमाएँ: केवल सरल एक्सप्रेशन, कोई सबक्वेरी नहीं

## IO मॉड्यूल
- **CsvReader/CsvWriter**: CSV फाइल पढ़ना और लिखना
- **JsonIO**: JSON (सामान्य/JSON Lines) पढ़ना और लिखना
- **SqliteIO**: SQLite टेबल पढ़ना, क्वेरी चलाना, DataFrame को टेबल में निर्यात करना

## API उदाहरण
```csharp
// CSV पढ़ना
var df = CsvReader.ReadCsv("data.csv");

// समूह और समेकन
var grouped = df.GroupBy("श्रेणी").Agg(new Dictionary<string, string[]> { ["मान"] = new[] { "sum", "mean" } });

// SQL क्वेरी
var universe = new DataUniverse();
universe.AddTable("तालिका", df);
var result = universe.SqlExecute("SELECT * FROM तालिका WHERE मान > 10");
```

## सीमाएँ और प्रदर्शन
- छोटे और मध्यम डेटा सेट (लगभग 1 मिलियन पंक्तियाँ) के लिए अनुकूलित
- मल्टीथ्रेडिंग या वितरित कंप्यूटिंग सपोर्ट नहीं
- कुछ ऑपरेशन (Pivot, Melt, जटिल Join) बड़े डेटा पर धीमे हो सकते हैं

## विस्तार और योगदान
- कोड विस्तार योग्य है: नए IO मॉड्यूल, इंडेक्स प्रकार, एग्रीगेशन मेथड जोड़े जा सकते हैं
- बाहरी DB के लिए SqliteIO या अपना IO मॉड्यूल बनाएं
