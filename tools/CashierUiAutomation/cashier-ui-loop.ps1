<#
  أتمتة شاشات الكاشير الحقيقي (WPF) بـWindows UI Automation - بند 3 من "خطة اختبار آلي موسّع" (8/10/2026).
  بلا حزم جديدة: UIAutomationClient جزء من ويندوز/.NET Framework (Windows PowerShell 5.1).
  - قاعدة اختبار منفصلة (اسمها لازم فيه "test")، الـAPI بيشتغل من مجلد bin تبعه بمتغيّرات بيئة (ما بنلمس appsettings).
  - الكاشير بينسخ لمجلد مؤقت (عنوان الـAPI بنسخة appsettings المؤقتة) وبياناته بـSPKT_CASHIER_DATA_DIR مؤقت - مش %LocalAppData%.
  - الباسوردات: SPKT_UI_ADMIN_PASSWORD (افتراضي 123 = bootstrap-admin على قاعدة فاضية)، SPKT_UI_CASHIER_PASSWORD (افتراضي عشوائي).
  - حلقة: بيع كاش (باركود عشوائي/كمية/كرتونة/عرض)، بيع بالدين، تعليق، "مزامنة الآن"، وقطع/وصل الـAPI عشوائيًا.
    بالآخر: الـAPI بيرجع، الطابور بيتفرّغ، ومقارنة سجل العمليات بالسيرفر (عدد الفواتير ومجموعها ومدفوعها).
  - أي نافذة رسالة غير متوقعة أو انهيار أو تعليق بينسجّل بالتفصيل بـui-log.txt.
#>
param(
    [int]$Operations = 40,
    [int]$Seed = 0,
    [int]$ApiPort = 5197,
    [string]$Database = "sprmrkt_ui_test",
    [string]$SqlServer = "localhost",
    # تشخيص: بيع بالدين وحيد وبعدها تفريغ شجرة UIA لكل النوافذ (لفهم شكل نافذة التأكيد).
    [switch]$DumpCreditDialog
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Data
Add-Type -Namespace SpktUi -Name Native -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern bool PostMessage(System.IntPtr hWnd, uint msg, System.IntPtr wParam, System.IntPtr lParam);
'@

if ($Database -notmatch 'test') { throw "اسم القاعدة لازم يحتوي 'test' - ما بنلمس قاعدة حقيقية." }
if ($Seed -eq 0) { $Seed = Get-Random -Minimum 1 -Maximum 2000000000 }
$rng = New-Object System.Random $Seed

$repo = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$apiDir = Join-Path $repo "SupermarketSystem.API\bin\Debug\net10.0"
$cashierBin = Join-Path $repo "SupermarketSystem.CashierApp\bin\Debug\net10.0-windows"
$work = Join-Path $env:TEMP ("spkt-ui-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
New-Item -ItemType Directory -Force -Path $work | Out-Null
$log = Join-Path $work "ui-log.txt"
$apiBase = "http://localhost:$ApiPort/api/v1"
$connectionString = "Server=$SqlServer;Database=$Database;Trusted_Connection=True;TrustServerCertificate=True;"

function Log([string]$text) {
    $line = "{0:HH:mm:ss.fff} {1}" -f (Get-Date), $text
    Add-Content -Path $log -Value $line -Encoding UTF8
    Write-Host $line
}
$findings = New-Object System.Collections.ArrayList
function Finding([string]$text) { [void]$findings.Add($text); Log "!!! $text" }

Log "SEED=$Seed ops=$Operations work=$work db=$Database"

# ---------------- الـAPI ----------------
$script:apiProcess = $null
function Start-Api {
    $env:ConnectionStrings__DefaultConnection = $connectionString
    if (-not $env:Jwt__SigningKey) { $env:Jwt__SigningKey = "ui-automation-only-signing-key-" + [guid]::NewGuid().ToString("N") }
    $env:ASPNETCORE_URLS = "http://localhost:$ApiPort"
    $env:RateLimiting__Enabled = "false"
    $env:Logging__File__Directory = Join-Path $work "api-logs"
    $stamp = (Get-Date).Ticks
    $script:apiProcess = Start-Process dotnet -ArgumentList "SupermarketSystem.API.dll" -WorkingDirectory $apiDir -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput (Join-Path $work "api-out-$stamp.txt") -RedirectStandardError (Join-Path $work "api-err-$stamp.txt")
    for ($i = 0; $i -lt 60; $i++) {
        Start-Sleep -Milliseconds 500
        try { Invoke-RestMethod "$apiBase/system/time-settings" -TimeoutSec 2 | Out-Null; Log "API up (pid $($script:apiProcess.Id))"; return } catch { }
    }
    throw "الـAPI ما اشتغل خلال 30 ثانية - شوف $work\api-err-*.txt"
}
function Stop-Api {
    if ($script:apiProcess -and -not $script:apiProcess.HasExited) { Stop-Process -Id $script:apiProcess.Id -Force; $script:apiProcess.WaitForExit() }
    $script:apiProcess = $null
    Log "API stopped"
}

$script:token = $null
function Api([string]$method, [string]$path, $body = $null) {
    $headers = @{}
    if ($script:token) { $headers.Authorization = "Bearer $($script:token)" }
    $params = @{ Method = $method; Uri = "$apiBase$path"; Headers = $headers; TimeoutSec = 30 }
    if ($null -ne $body) {
        $params.Body = [Text.Encoding]::UTF8.GetBytes(($body | ConvertTo-Json -Depth 8))
        $params.ContentType = "application/json; charset=utf-8"
    }
    try { return Invoke-RestMethod @params }
    catch {
        $sent = if ($null -ne $body) { ($body | ConvertTo-Json -Depth 8 -Compress) } else { "" }
        throw "$method $path -> $($_.Exception.Message) $($_.ErrorDetails.Message) | body=$($sent.Substring(0, [Math]::Min(600, $sent.Length)))"
    }
}

function Sql([string]$query) {
    $connection = New-Object System.Data.SqlClient.SqlConnection $connectionString
    $connection.Open()
    try {
        $command = $connection.CreateCommand(); $command.CommandText = $query
        $table = New-Object System.Data.DataTable
        $table.Load($command.ExecuteReader())
        return , $table
    } finally { $connection.Close() }
}

# ---------------- البذر (عبر الـAPI) ----------------
function Seed-Data {
    try { Api POST "/system/bootstrap-admin" @{} | Out-Null; Log "bootstrap-admin done" } catch { Log "bootstrap-admin skipped: $($_.Exception.Message)" }
    $adminPassword = if ($env:SPKT_UI_ADMIN_PASSWORD) { $env:SPKT_UI_ADMIN_PASSWORD } else { "123" }
    $login = Api POST "/auth/login" @{ username = "admin"; password = $adminPassword; appType = "Admin" }
    $script:token = $login.accessToken
    $branchId = $login.branchId
    if (-not $branchId) { $branchId = (Api GET "/branches?pageSize=10").items[0].id }

    $suffix = (Get-Random -Minimum 10000 -Maximum 99999).ToString()
    $category = Api POST "/product-categories" @{ name = "تصنيف أتمتة $suffix"; parentCategoryId = $null }

    $products = @()
    $defs = @(
        @{ name = "حليب أتمتة"; price = 1.250; units = @(@{ u = "حبة"; f = 1 }, @{ u = "كرتونة"; f = 12 }) },
        @{ name = "مياه أتمتة"; price = 0.350; units = @(@{ u = "قنينة"; f = 1 }) },
        @{ name = "رز أتمتة"; price = 3.500; units = @(@{ u = "كيس"; f = 1 }) },
        @{ name = "شيبس أتمتة"; price = 0.250; units = @(@{ u = "كيس"; f = 1 }) }
    )
    $n = 0
    foreach ($d in $defs) {
        $n++
        $barcodes = @(); $units = @()
        foreach ($u in $d.units) {
            $code = "77" + $suffix + $n + ([int]$u.f).ToString("00")
            $units += @{ unitName = $u.u; conversionFactorToBase = [decimal]$u.f; isBaseUnit = ($u.f -eq 1) }
            $barcodes += @{ barcodeValue = $code; unitName = $u.u }
        }
        $created = Api POST "/products" @{ name = "$($d.name) $suffix"; description = $null; categoryId = $category.categoryId; isBatchTracked = $false;
            suggestedRetailPrice = $null; expectedShelfLifeDays = $null; units = $units; barcodes = $barcodes }
        Api POST "/products/$($created.productId)/branches" @{ branchId = $branchId; sellingPrice = [decimal]$d.price; minimumStock = 0; maximumStock = $null } | Out-Null
        $unitRows = @(Api GET "/products/$($created.productId)/units" | ForEach-Object { $_ })
        $products += [pscustomobject]@{ Id = $created.productId; Name = $d.name; Price = [decimal]$d.price; Units = $unitRows; Barcodes = $barcodes }
    }

    # مخزون بفاتورة شراء (رصيد وتكلفة)
    $supplier = Api POST "/suppliers" @{ name = "مورد أتمتة $suffix"; contactName = $null; phone = "0790$suffix"; email = $null; street = $null; city = "عمّان"; postalCode = $null; country = "الأردن" }
    $lines = @()
    foreach ($p in $products) {
        $base = $p.Units | Where-Object { $_.conversionFactorToBase -eq 1 } | Select-Object -First 1
        $lines += @{ productId = $p.Id; productUnitId = $base.id; quantity = 500; unitCost = [decimal]::Round($p.Price * 0.7, 3); existingProductBatchId = $null; newBatchNumber = $null; newBatchExpiryDate = $null }
    }
    Api POST "/purchase-invoices" @{ branchId = $branchId; supplierId = $supplier.supplierId; supplierInvoiceReference = "UI-$suffix"; items = $lines; imageReferences = $null; dueDate = $null } | Out-Null

    # عرض كمية على المياه: 3 بـ0.900
    Api POST "/products/$($products[1].Id)/promotions" @{ title = "3 مياه بـ0.900"; bundleQuantity = 3; bundlePrice = 0.900; maxQuantityPerInvoice = $null;
        startAtUtc = (Get-Date).ToUniversalTime().AddHours(-1).ToString("o"); endAtUtc = (Get-Date).ToUniversalTime().AddDays(5).ToString("o"); branchIds = @($branchId) } | Out-Null

    # كاشير
    $roles = @(Api GET "/users/roles" | ForEach-Object { $_ })
    if ($roles.Count -eq 1 -and $roles[0].items) { $roles = @($roles[0].items) }
    $cashierRole = ($roles | Where-Object { $_.name -eq "كاشير" } | Select-Object -First 1).id
    $script:cashierUser = "ui.cashier.$suffix"
    $script:cashierPassword = if ($env:SPKT_UI_CASHIER_PASSWORD) { $env:SPKT_UI_CASHIER_PASSWORD } else { "Ui_" + [guid]::NewGuid().ToString("N").Substring(0, 12) + "!" }
    Api POST "/users" @{ fullName = "كاشير أتمتة"; username = $script:cashierUser; email = "$($script:cashierUser)@test.local"; password = $script:cashierPassword; roleId = $cashierRole; branchId = $branchId } | Out-Null

    # زبون دفتر (ما في endpoint إداري لإنشاء زبون - إدخال مباشر بقاعدة الاختبار)
    $script:creditPhone = "0795" + $suffix + "1"
    Sql "INSERT INTO Customers (Id, FullName, Phone, Email, IsDeleted, IsBlocked, CreatedAtUtc) VALUES (NEWID(), N'زبون دفتر أتمتة', '$($script:creditPhone)', NULL, 0, 0, SYSUTCDATETIME())" | Out-Null

    $script:branchId = $branchId
    $script:products = $products
    $script:token = $null
    Log "seeded: branch=$branchId cashier=$($script:cashierUser) products=$($products.Count) creditPhone=$($script:creditPhone)"
}

# ---------------- UI Automation ----------------
$AE = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]
function Cond($property, $value) { New-Object System.Windows.Automation.PropertyCondition($property, $value) }

function Get-AppWindows {
    if (-not $script:cashier -or $script:cashier.HasExited) { return @() }
    return @($AE::RootElement.FindAll($TS::Children, (Cond $AE::ProcessIdProperty $script:cashier.Id)))
}
function Find-Id([string]$id, [int]$timeoutMs = 8000) {
    $deadline = (Get-Date).AddMilliseconds($timeoutMs)
    do {
        foreach ($w in Get-AppWindows) {
            $el = $w.FindFirst($TS::Descendants, (Cond $AE::AutomationIdProperty $id))
            if ($el) { return $el }
        }
        Start-Sleep -Milliseconds 150
    } while ((Get-Date) -lt $deadline)
    return $null
}
function Need-Id([string]$id, [int]$timeoutMs = 8000) {
    $el = Find-Id $id $timeoutMs
    if (-not $el) { throw "عنصر '$id' مش ظاهر خلال $timeoutMs ms" }
    return $el
}
function Invoke-Id([string]$id) { (Need-Id $id).GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
function Set-Id([string]$id, [string]$text) { (Need-Id $id).GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($text) }
function Text-Id([string]$id) {
    $el = Find-Id $id 3000
    if (-not $el) { return "" }
    try { return $el.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value } catch { return $el.Current.Name }
}

# نوافذ رسائل (MessageBox = #32770): بنسجّل النص وبنكبس الزر المطلوب.
function Handle-Dialogs([string]$Answer = "OK", [switch]$Expected) {
    $handled = @()
    # MessageBox.Show(this, ...) = نافذة مملوكة، بتطلع بشجرة UIA تحت نافذة صاحبها مش بالجذر.
    $dialogs = @()
    foreach ($top in Get-AppWindows) {
        if ($top.Current.ClassName -eq "#32770") { $dialogs += $top }
        $dialogs += @($top.FindAll($TS::Descendants, (Cond $AE::ClassNameProperty "#32770")))
    }
    foreach ($w in $dialogs) {
        # نص الرسالة بعنصر Static (أزرار MessageBox ونصّها بيطلعوا Pane بلا أنماط UIA على هالنسخة).
        $texts = $w.FindAll($TS::Descendants, (Cond $AE::ClassNameProperty "Static")) | ForEach-Object { $_.Current.Name } | Where-Object { $_ }
        $message = (($texts -join " | ") -replace "\s+", " ")
        $handled += "$($w.Current.Name): $message"
        if (-not $Expected) { Finding "رسالة غير متوقعة: [$($w.Current.Name)] $message" } else { Log "dialog: [$($w.Current.Name)] $message" }
        # الجواب بـWM_COMMAND مباشرة للنافذة (Invoke مش مدعوم على أزرارها). لو الزر المطلوب مش موجود: OK ثم Cancel.
        $ids = @{ OK = 1; Cancel = 2; Yes = 6; No = 7 }
        $present = @($w.FindAll($TS::Descendants, (Cond $AE::ClassNameProperty "Button")) | ForEach-Object { [int]$_.Current.AutomationId })
        $choice = @($ids[$Answer], 1, 2) | Where-Object { $present -contains $_ } | Select-Object -First 1
        if ($choice) { [void][SpktUi.Native]::PostMessage([IntPtr]$w.Current.NativeWindowHandle, 0x0111, [IntPtr]$choice, [IntPtr]::Zero) }
        Start-Sleep -Milliseconds 400
    }
    return $handled
}

function Check-Alive([string]$where) {
    if ($script:cashier.HasExited) {
        Finding "الكاشير انهار ($where) - exit code $($script:cashier.ExitCode)"
        $events = Get-WinEvent -FilterHashtable @{ LogName = "Application"; StartTime = (Get-Date).AddMinutes(-3) } -ErrorAction SilentlyContinue |
            Where-Object { $_.ProviderName -in @(".NET Runtime", "Application Error") } | Select-Object -First 2
        foreach ($e in $events) { Finding ("EventLog: " + ($e.Message -replace "\s+", " ").Substring(0, [Math]::Min(1500, $e.Message.Length))) }
        return $false
    }
    $script:cashier.Refresh()
    if (-not $script:cashier.Responding) { Finding "الكاشير مش مستجيب ($where)" }
    return $true
}

# ---------------- تشغيل الكاشير ----------------
function Start-Cashier {
    $copy = Join-Path $work "cashier"
    if (-not (Test-Path $copy)) {
        Copy-Item $cashierBin $copy -Recurse
        $settingsPath = Join-Path $copy "appsettings.json"
        $settings = Get-Content $settingsPath -Raw -Encoding UTF8 | ConvertFrom-Json
        $settings.ApiBaseUrl = $apiBase
        $settings.SyncIntervalSeconds = 15
        [IO.File]::WriteAllText($settingsPath, ($settings | ConvertTo-Json -Depth 5), (New-Object Text.UTF8Encoding $false))
    }
    $env:SPKT_CASHIER_DATA_DIR = Join-Path $work "cashier-data"
    $script:cashier = Start-Process (Join-Path $copy "SupermarketSystem.CashierApp.exe") -PassThru
    Log "cashier started pid $($script:cashier.Id) data=$($env:SPKT_CASHIER_DATA_DIR)"
    Start-Sleep -Seconds 2
    if ($script:cashier.HasExited) { throw "الكاشير سكّر فورًا بعد التشغيل (exit $($script:cashier.ExitCode)) - Smart App Control؟" }
    Need-Id "UsernameBox" 30000 | Out-Null
    Handle-Dialogs | Out-Null
    Set-Id "UsernameBox" $script:cashierUser
    Set-Id "PasswordBox" $script:cashierPassword
    Invoke-Id "LoginButton"
    if (-not (Find-Id "StartSaleButton" 20000)) {
        Handle-Dialogs | Out-Null
        throw "الدخول ما نجح: '$(Text-Id 'ErrorText')' / '$(Text-Id 'StatusText')'"
    }
    Invoke-Id "StartSaleButton"
    Need-Id "BarcodeBox" 20000 | Out-Null
    Start-Sleep -Seconds 3
    Handle-Dialogs | Out-Null
    Log "sale window open"
}

function Select-Cash {
    $combo = Need-Id "PaymentMethodCombo"
    $expand = $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    $expand.Expand(); Start-Sleep -Milliseconds 300
    $items = @($combo.FindAll($TS::Descendants, (Cond $AE::ControlTypeProperty ([System.Windows.Automation.ControlType]::ListItem))))
    $cash = $items | Where-Object { $_.Current.Name -match "كاش|نقد|Cash" } | Select-Object -First 1
    if (-not $cash) { $cash = $items | Select-Object -First 1 }
    if (-not $cash) { Finding "قائمة طرق الدفع فاضية"; $expand.Collapse(); return $false }
    $cash.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    $expand.Collapse()
    return $true
}

function Parse-Total { $t = Text-Id "TotalText"; if ($t -match "([\d.]+)") { return [decimal]$Matches[1] } return [decimal]-1 }

function Scan-Random {
    $p = $script:products[$rng.Next($script:products.Count)]
    $b = $p.Barcodes[$rng.Next($p.Barcodes.Count)]
    $qty = @(1, 1, 1, 2, 3, 4)[$rng.Next(6)]
    $text = if ($qty -gt 1) { "$qty*$($b.barcodeValue)" } else { $b.barcodeValue }
    Set-Id "BarcodeBox" $text
    Invoke-Id "AddButton"
    Start-Sleep -Milliseconds 300
    return "$($p.Name)/$($b.unitName)x$qty"
}

$script:completed = New-Object System.Collections.ArrayList   # بيعات الكاشير قال إنها تمت
function Do-Sale([switch]$Credit) {
    $lines = 1 + $rng.Next(3)
    $desc = @(); for ($i = 0; $i -lt $lines; $i++) { $desc += Scan-Random }
    $total = Parse-Total
    if ($total -le 0) { Finding "إجمالي السلة $total بعد مسح ($($desc -join ', ')) error='$(Text-Id 'ErrorText')'"; Handle-Dialogs | Out-Null; return }
    if (-not (Select-Cash)) { return }
    $before = Text-Id "SaleStatusText"
    if ($Credit) {
        $paid = [decimal]::Round($total / 3, 3)
        Set-Id "CustomerPhoneBox" $script:creditPhone
        Set-Id "TenderedAmountBox" ($paid.ToString([Globalization.CultureInfo]::InvariantCulture))
        Invoke-Id "CreditSaleButton"
        Start-Sleep -Seconds 2
        $d = Handle-Dialogs -Answer Yes -Expected
        if ($d.Count -eq 0) {
            Log "credit sale not confirmed (error='$(Text-Id 'ErrorText')') - clearing cart"
            Set-Id "CustomerPhoneBox" ""; Set-Id "TenderedAmountBox" ""
            Invoke-Id "CompleteSaleButton"; $amount = $total; $Credit = $false
        } else { $amount = $paid }
    } else {
        Set-Id "CustomerPhoneBox" ""
        Set-Id "TenderedAmountBox" ""
        Invoke-Id "CompleteSaleButton"
        $amount = $total
    }
    $deadline = (Get-Date).AddSeconds(25)
    do { Start-Sleep -Milliseconds 300; $status = Text-Id "SaleStatusText" } while (($status -eq $before -or $status -notmatch "✓|⚠") -and (Get-Date) -lt $deadline)
    Handle-Dialogs | Out-Null
    if ($status -match "✓" -and $status -ne $before) {
        [void]$script:completed.Add([pscustomobject]@{ Total = $total; Paid = $amount; Credit = [bool]$Credit; Online = ($status -match "انبعت للسيرفر") })
        Log ("sale {0} total={1} paid={2} [{3}] -> {4}" -f ($(if ($Credit) { "CREDIT" } else { "CASH" })), $total, $amount, ($desc -join ', '), $status)
    } else {
        Finding "البيعة ما خلصت خلال 25 ثانية: status='$status' error='$(Text-Id 'ErrorText')' items=[$($desc -join ', ')]"
    }
}

# ---------------- التشغيل ----------------
$exitCode = 0
try {
    Start-Api
    Seed-Data
    Start-Cashier
    $apiUp = $true
    if ($DumpCreditDialog) {
        Scan-Random | Out-Null
        Set-Id "CustomerPhoneBox" $script:creditPhone
        Invoke-Id "CreditSaleButton"
        Start-Sleep -Seconds 3
        $walker = [System.Windows.Automation.TreeWalker]::RawViewWalker
        function Dump($el, $depth) {
            if ($depth -gt 6 -or -not $el) { return }
            $c = $el.Current
            $patterns = ($el.GetSupportedPatterns() | ForEach-Object { $_.ProgrammaticName -replace 'PatternIdentifiers.Pattern', '' }) -join ','
            Log ("{0}[{1}] class='{2}' id='{3}' name='{4}' patterns={5}" -f ('  ' * $depth), $c.ControlType.ProgrammaticName, $c.ClassName, $c.AutomationId, $c.Name, $patterns)
            $child = $walker.GetFirstChild($el)
            while ($child) { if ($c.ClassName -ne 'DataGrid') { Dump $child ($depth + 1) }; $child = $walker.GetNextSibling($child) }
        }
        foreach ($w in Get-AppWindows) { Dump $w 0 }
        $Operations = 0
    }
    for ($op = 1; $op -le $Operations; $op++) {
        if (-not (Check-Alive "op $op")) { Start-Cashier }
        # نافذة رسالة مفتوحة = لا تكبس شي ورا ظهرها (UIA بيقدر يكبس أزرار الشاشة حتى لو في نافذة modal).
        Handle-Dialogs | Out-Null
        $roll = $rng.Next(100)
        try {
            if ($roll -lt 60) { Do-Sale }
            elseif ($roll -lt 70) { if ($apiUp) { Do-Sale -Credit } else { Do-Sale } }
            elseif ($roll -lt 78) { Scan-Random | Out-Null; Invoke-Id "HoldButton"; Start-Sleep -Milliseconds 800; Handle-Dialogs Yes -Expected | Out-Null; Log "held cart" }
            elseif ($roll -lt 84) { if ($apiUp) { Invoke-Id "SyncNowButton"; Start-Sleep -Seconds 3; Handle-Dialogs -Expected | Out-Null; Log "sync now -> '$(Text-Id 'ConnectionStatusText')'" } }
            else { if ($apiUp) { Stop-Api; $apiUp = $false } else { Start-Api; $apiUp = $true } }
        } catch {
            Finding "op $op خطأ أتمتة: $($_.Exception.Message)"
            Handle-Dialogs | Out-Null
        }
    }

    if (-not $apiUp) { Start-Api }
    Log "draining queue..."
    $expected = $script:completed.Count
    $deadline = (Get-Date).AddMinutes(3)
    do {
        try { Invoke-Id "SyncNowButton" } catch { }
        Start-Sleep -Seconds 6
        Handle-Dialogs -Expected | Out-Null
        $count = (Sql "SELECT COUNT(*) AS c FROM SaleInvoices s JOIN Users u ON u.Id = s.CreatedByUserId WHERE u.Username = '$($script:cashierUser)'").Rows[0].c
    } while ($count -lt $expected -and (Get-Date) -lt $deadline)

    $server = (Sql "SELECT COUNT(*) AS c, ISNULL(SUM(s.TotalAmount),0) AS t, ISNULL(SUM(s.TotalPaidAmount),0) AS p FROM SaleInvoices s JOIN Users u ON u.Id = s.CreatedByUserId WHERE u.Username = '$($script:cashierUser)'").Rows[0]
    $localTotal = [decimal](($script:completed | Measure-Object Total -Sum).Sum)
    $localPaid = [decimal](($script:completed | Measure-Object Paid -Sum).Sum)
    Log ("RESULT local: {0} sales total={1} paid={2} | server: {3} invoices total={4} paid={5}" -f $expected, $localTotal, $localPaid, $server.c, $server.t, $server.p)
    if ($server.c -ne $expected) { Finding "عدد الفواتير بالسيرفر ($($server.c)) ≠ بيعات الكاشير ($expected)" }
    if ([decimal]$server.t -ne $localTotal) { Finding "مجموع الفواتير بالسيرفر ($($server.t)) ≠ مجموع شاشة الكاشير ($localTotal)" }
    if ([decimal]$server.p -ne $localPaid) { Finding "المدفوع بالسيرفر ($($server.p)) ≠ المدفوع بالكاشير ($localPaid)" }
    $rejected = (Sql "SELECT COUNT(*) AS c FROM RejectedSaleAttempts r JOIN Users u ON u.Id = r.CashierUserId WHERE u.Username = '$($script:cashierUser)'").Rows[0].c
    if ($rejected -gt 0) { Finding "في $rejected بيعة مرفوضة بالسيرفر (RejectedSaleAttempts)" }
}
catch {
    Finding "توقف السكربت: $($_.Exception.Message)"
    $exitCode = 2
}
finally {
    if ($script:cashier -and -not $script:cashier.HasExited) { Stop-Process -Id $script:cashier.Id -Force }
    Stop-Api
    Log "SEED=$Seed findings=$($findings.Count) log=$log"
    $findings | ForEach-Object { Write-Host " - $_" }
}
if ($findings.Count -gt 0 -and $exitCode -eq 0) { $exitCode = 1 }
exit $exitCode
