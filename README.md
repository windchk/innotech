# Innotech SAP API

.NET 10 บน Windows x64 ใช้ SAP Business One DI API สำหรับ **Update U_OLDCODE ระดับ head และ U_SO_Ref / U_So_Line_Ref ทุก line แล้ว Cancel SO ใบเดียวกัน** พร้อม log ใน TEMPAPI

## โค้ดหลัก

```text
src/Innotech.Sap.Api/
  Controllers/ORDRController.cs
  Models/ORDR.cs
  Models/JSON_Response.cs
  Models/DIHelper.cs
  Program.cs
  appsettings.json
```

Controller ใช้รูปแบบตัวอย่าง: List<Sales_Order>, try ครอบ foreach, JsonConvert.SerializeObject, QuerySql, ExecuteSql, Connect, Documents.GetByKey, UserFields.Fields.Item(...).Value, Update(), Cancel(), GetLastError, if/else สร้าง response และ return Ok(Lresponse)

DIHelper ใช้ฟังก์ชันจากตัวอย่าง: Connect, RunDicTionry, GetTable, DoQuery, QuerySql และ ExecuteSql โดยเปลี่ยนค่าเชื่อมต่อมาอ่าน appsettings.json ใช้ System.Data.SqlClient ตามตัวอย่าง

ส่วนที่ปรับสำหรับ .NET 10 คือ Controller/IActionResult, Route("API"), Program.cs และการตั้ง Newtonsoft.Json ให้คงชื่อ property ตาม model ส่วน finally ใช้ GC.Collect() และ Dispose() ตามตัวอย่าง

## เรียก API

GET /API/TestConnectSAP — ไม่ต้องส่ง payload หรือ API key เรียก Connect() ด้วย config เดิม ถ้าเชื่อมต่อสำเร็จจะคืน CompanyDB, CompanyName แล้ว Disconnect() โดยไม่แก้เอกสาร SAP

```json
{
  "Status": "Success",
  "CompanyDB": "DB_UAT_2",
  "CompanyName": "ชื่อบริษัทใน SAP",
  "errCode": 0,
  "errMsg": ""
}
```

ถ้า SAP ปฏิเสธการเชื่อมต่อจะคืน Status = Error พร้อม errCode/errMsg จาก DIHelper ถ้าเกิด exception จะคืน Exception Error ทั้งหมดตอบ HTTP 200 แบบตัวอย่าง ให้ตรวจ Status ใน response

POST /API/SalesOrder/Cancel — Content-Type: application/json — ไม่ใช้ API key

ส่งเป็น array แบบตัวอย่าง:

```json
[
  {
    "DocEntry": 1234,
    "U_OLDCODE": "OLD-SO-001",
    "U_SO_Ref": "SO-REF-001",
    "U_So_Line_Ref": 5
  }
]
```

DocEntry คือ SO ที่จะ Update UDF แล้ว Cancel ใบเดียวกัน โดย U_OLDCODE อัปเดตที่ head ส่วน U_SO_Ref และ U_So_Line_Ref ใช้ค่าสองค่าจาก payload อัปเดตทุก line ไม่ต้องส่ง Lines หรือ LineNum

รับเลข SO จาก DocEntry ใน payload เท่านั้น และต้องมากกว่า 0 ส่วน DocNum อ่านจากเอกสารใน SAP เพื่อแสดง SO_DocNum ใน response และเก็บ DocNum_ORDR ใน log

รับค่าจาก payload โดยตรง: U_OLDCODE เป็นข้อความที่ head, U_SO_Ref เป็นข้อความยาวไม่เกิน 20 ตัวอักษร และ U_So_Line_Ref เป็นเลขจำนวนเต็มชนิด smallint ช่วง -32768 ถึง 32767 ตาม schema ที่ส่งมา ทุก line ใช้ค่า U_SO_Ref และ U_So_Line_Ref เดียวกันตาม payload

ส่ง U_So_Line_Ref เป็น JSON number เช่น 0 หรือ 5 โดยไม่ใส่เครื่องหมายคำพูด ค่า "TEST-001" และข้อความว่าง "" ใช้เป็นค่าตัวเลขไม่ได้ ถ้าต้องการตั้งเป็นศูนย์ให้ส่ง 0 อย่างชัดเจน ส่วน U_OLDCODE และ U_SO_Ref ส่งข้อความว่างได้ และข้อความอย่าง "005" ในช่องข้อความจะคงเลขศูนย์นำหน้า

ต้องส่ง UDF ทั้งสามค่า ถ้าขาดค่าหรือส่ง null จะหยุดก่อน Update/Cancel โค้ดวนทุก line ที่อยู่ใน SO ด้วย SetCurrentLine(i) และไม่เลือก line จาก payload

ถ้า U_So_Line_Ref ไม่ใช่เลขจำนวนเต็มหรือเกินช่วง smallint หรือ U_SO_Ref ยาวเกิน 20 ตัวอักษร ASP.NET จะตอบ HTTP 400 พร้อม errors ระบุชื่อ field ก่อนเข้า action จึงไม่เชื่อม SAP และไม่สร้าง log ของ action สำหรับ payload ที่ผิดชนิด

ตัวอย่าง payload ที่ใช้ DocEntry อยู่ใน examples/cancel-sales-order.http

Response:

```json
[
  {
    "SO_DocNum": "26000123",
    "Status": "Success",
    "errCode": 0,
    "errMsg": ""
  }
]
```

Status ใช้ Success, Error หรือ Exception Error และตอบ Ok(Lresponse) แบบตัวอย่าง ถ้า SAP คืน error จะทำ Head ถัดไป ถ้าเกิด exception จะเข้า catch ด้านนอกและจบ batch

## Log และลำดับทำงาน

1. JsonConvert.SerializeObject(Head) เก็บ payload ของรายการปัจจุบัน
2. QuerySql ใช้ INSERT ... OUTPUT INSERTED.Log_ID ลง Log_ORDR ก่อนเข้า SAP โดย Log_ID เป็นเลข IDENTITY ที่ SQL สร้างให้แต่ละรายการ
3. ตรวจ DocEntry จาก payload แล้ว Connect และใช้ GetByKey(DocEntry) โหลด SO
4. กำหนด oDoc.UserFields.Fields.Item("U_OLDCODE").Value = Head.U_OLDCODE แล้ววนทุก line เพื่อกำหนด oDoc.Lines.UserFields.Fields.Item("U_SO_Ref").Value = Head.U_SO_Ref และ Item("U_So_Line_Ref").Value = Convert.ToInt32(Head.U_So_Line_Ref) ก่อนเรียก Update() ครั้งเดียว
5. ถ้า Update() สำเร็จจึงเรียก Cancel() บน SO ใบเดียวกัน
6. สร้าง response และ ExecuteSql UPDATE ผล log เป็น F_SAP = Y หรือ N โดยใช้ Where Log_ID พร้อมบันทึก DocEntry และ DocNum_ORDR ที่ได้จาก SAP

ไม่มี transaction เพิ่มจากตัวอย่าง ดังนั้นถ้า Update UDF สำเร็จแล้ว Cancel ไม่สำเร็จ ค่า UDF ที่อัปเดตไปจะคงอยู่ หาก INSERT log ไม่สำเร็จจะหยุดก่อน Connect SAP ส่วน ExecuteSql พิมพ์ exception ตามตัวอย่าง จึงไม่มีการยืนยันผล log เพิ่มหรือ rollback เมื่ออัปเดตผล log ไม่สำเร็จ

sql/Create_TEMPAPI.sql เป็นสคริปต์สำหรับสร้างฐานข้อมูลและตารางครั้งแรกผ่าน SSMS โดย API นี้ใช้ Log_ORDR ซึ่งมี Log_ID เป็นเลขบันทึกอัตโนมัติ, DocEntry, DocNum_ORDR, F_WebDate, F_SAPDate, F_SAP, F_MESSAGE และ Json

สคริปต์ sql/Create_TEMPAPI.sql สร้างฐานข้อมูล TEMPAPI และตาราง Log_ORDR ตาม DDL ที่ให้มา ให้รันผ่าน SSMS สำหรับฐานข้อมูลใหม่ก่อนเรียก API สคริปต์ไม่ถูกรันอัตโนมัติ

ส่ง SO เดิมหลายครั้งจะได้ Log_ID ใหม่ทุกครั้ง และแต่ละแถวเก็บ payload ของรายการนั้นเอง Response มี SO_DocNum, Status, errCode และ errMsg

## Config

ทุกค่าอยู่ใน src/Innotech.Sap.Api/appsettings.json ไฟล์เดียว และอ่านใหม่เมื่อ restart แอป:

- Sap: การเชื่อมต่อ DI API, Server 192.168.10.207, CompanyDb DB_UAT_2 และ SQL Server 2022 ตามภาพหน้าจอ พร้อมบัญชีที่ให้ไว้
- ConnectionStrings: มี 2 รายการ คือ DB_UAT_2 ชี้ฐานข้อมูล SAP และ templog ชี้ TEMPAPI สำหรับ log
- DIHelper ใช้ TempOrSap.DB_UAT_2 สำหรับฐานข้อมูล SAP และ TempOrSap.templog สำหรับ log โดยชื่อ connection variable คือ constrDB_UAT_2 และ constrtemplog
- Interface: LogTable
- Sql: CommandTimeoutSeconds
- Urls, Logging, AllowedHosts: ค่า host ของ API

Sap:DbServerType ใช้ตัวเลข 17 ซึ่งตรงกับ dst_MSSQL2022 ตาม [SAP SDK](https://help.sap.com/doc/089315d8d0f8475a9fc84fb919b501a3/10.0/en-US/SDKHelp/SAPbobsCOM~Enumerations~BoDataServerTypes_EN.html) เพราะ interop DLL จากตัวอย่างยังไม่มีชื่อ enum นี้ โดย Enum.Parse ใน DIHelper อ่านค่าตัวเลขได้ การเชื่อมต่อจริงต้องใช้ DI API ที่รองรับ SQL Server 2022 บนเครื่องที่รัน API

## Build และตรวจสอบ

ต้องมี Windows x64, .NET 10 และ SAP Business One DI API 64-bit ที่ตรงกับ SAP ที่ใช้งาน ไฟล์ lib/Interop.SAPbobsCOM.dll มาจากโปรเจกต์ตัวอย่าง

```powershell
dotnet build Innotech.Sap.sln -c Release
dotnet test Innotech.Sap.sln -c Release
dotnet run --project src/Innotech.Sap.Api
dotnet publish src/Innotech.Sap.Api -c Release -p:PublishProfile=IIS
```

Publish Profile อยู่ที่ src/Innotech.Sap.Api/Properties/PublishProfiles/IIS.pubxml กำหนด Release, win-x64 และปลายทาง C:\API Publish\SalesOrder โดยไฟล์ publish จะมี web.config สำหรับ IIS, ตัว API, appsettings.json และ sql/Create_TEMPAPI.sql ให้ตั้ง Physical Path ของเว็บไซต์หรือ Application ใน IIS เป็นโฟลเดอร์นี้

เครื่องนี้มี .NET 10 SDK ที่ %LOCALAPPDATA%\Codex\sdks\innotech-dotnet10 ถ้า dotnet ใน PATH ยังเป็นรุ่นก่อนให้เรียก dotnet.exe ในโฟลเดอร์นี้โดยตรง

ชุดทดสอบ 39 กรณี compile Controller และ model จาก source จริง โดยใช้ DIHelper จำลองเฉพาะในโปรเจกต์ test ไม่มี test hook ใน source ของแอป ตรวจการรับ DocEntry เท่านั้น, อ่าน DocNum จาก SAP, การอัปเดต U_OLDCODE ที่ head และ UDF ทั้งสองช่องทุก line ก่อน Cancel SO ใบเดียวกัน, เลข line ไม่ต่อเนื่อง, payload ไม่ครบ, Update/Cancel error, response 4 ช่อง, Log_ID แยกแต่ละรายการ, INSERT log ไม่สำเร็จ, การส่ง SO เดิมซ้ำ และ TestConnectSAP

HTTP tests ใช้ TestServer ในหน่วยความจำกับ MVC/Newtonsoft และ source Controller จริง ตรวจ payload "TEST-001" ใน U_So_Line_Ref ให้ตอบ 400, ค่าเกินช่วง/ทศนิยม/boolean, ขอบเขต smallint และ U_SO_Ref ยาว 20/21 ตัวอักษร SDK จำลองตรวจว่า U_So_Line_Ref ถูกส่งเป็น integer การทดสอบไม่เรียก SAP หรือฐานข้อมูลจริง
