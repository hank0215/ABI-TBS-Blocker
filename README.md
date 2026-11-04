# ABI-TBS-Blocker

《暗區突圍：無限》一鍵封鎖啟動器內建瀏覽器 `tbs_browser.exe` 的小工具。

用 Windows 防火牆的「輸出規則」禁止 `tbs_browser.exe` 連上網路，按一下就能封鎖或解除，不會修改任何遊戲檔案。

**[下載最新版](https://github.com/hank0215/ABI-TBS-Blocker/releases/latest)**

## 為什麼要做這個

暗區的啟動器內建一個瀏覽器 `tbs_browser.exe`，用來顯示啟動器裡的網頁內容（公告、活動等）。

2026 年 11 月，同樣由 Level Infinite 發行的《勝利女神：妮姬》電腦版，被發現啟動器內建瀏覽器載入的網頁檔案遭人竄改，部分玩家因此被裝上木馬。

目前沒有證據顯示暗區受到影響。這個工具是**預防用**的：就算以後網頁檔案又被竄改，被封鎖的 `tbs_browser.exe` 也載入不了。

## 怎麼用

1. 完全關閉暗區啟動器（右下角工作列圖示按右鍵 → 結束）
2. 到 [Releases](https://github.com/hank0215/ABI-TBS-Blocker/releases/latest) 下載 `ABI-TBS-Blocker.exe`，按兩下執行，Windows 詢問是否允許變更時按「是」
3. 工具會自動找到 `tbs_browser.exe`，按「封鎖」
4. 重新開啟暗區啟動器，確認能正常登入、更新、開始遊戲

找不到檔案時，按「手動選擇檔案…」，選遊戲資料夾裡的 `launcher\service\tbs_browser.exe`。

> 跳出「Windows 已保護您的電腦」：這個 exe 沒有付費的數位簽章，按「其他資訊」→「仍要執行」即可。

## 副作用和還原

- 啟動器裡的網頁內容（公告、活動頁面等）可能變成空白
- 如果啟動器因此無法登入、更新或開始遊戲：重新打開工具按「解除封鎖」，就會完全恢復原狀
- 遊戲改版後如果 `tbs_browser.exe` 換了位置，封鎖會失效，重新執行一次工具即可

## 這個工具做了什麼

- 只新增或刪除一條 Windows 防火牆輸出規則：`Block tbs_browser.exe (Arena Breakout Infinite)`
- 只處理暗區的 `tbs_browser.exe`，不會動到其他遊戲
- 不修改、不刪除、不下載任何檔案；本身不連網、不常駐、不會開機自動執行
- 規則可以在「Windows 安全性 → 防火牆與網路保護 → 進階設定 → 輸出規則」看到，也可以在那裡手動刪除
- 使用防毒軟體附的防火牆（不是 Windows 防火牆）時，規則可能不會生效，工具畫面會提醒

偵測方式：讀取暗區在登錄檔寫入的安裝位置（`GameInstallPath`），再搜尋各磁碟常見的安裝資料夾。

## 確認下載的檔案沒被調包

每個 Release 的說明都附有 SHA256。在 exe 所在資料夾開啟 PowerShell，輸入：

```powershell
Get-FileHash .\ABI-TBS-Blocker.exe
```

結果要和 Release 說明裡的一致。

## 自己從原始碼編譯

不想相信別人編好的 exe，可以自己編譯。Windows 10/11 內建 .NET Framework 4 的 C# 編譯器，不用安裝任何東西：

1. 下載這個倉庫（Code → Download ZIP）並解壓縮
2. 執行 `build.bat`
3. 編譯結果在 `dist\ABI-TBS-Blocker.exe`

自己編譯的 exe 雜湊值會和 Release 的不同，這是正常的（編譯器每次都會寫入不同的時間戳記）。

## 命令列

給進階使用者。需要系統管理員權限，結果寫到 `/report` 指定的檔案：

```
ABI-TBS-Blocker.exe /status  /report r.txt
ABI-TBS-Blocker.exe /block   /report r.txt   [/path 其他位置\tbs_browser.exe]
ABI-TBS-Blocker.exe /unblock /report r.txt
```

結束代碼：0 成功、1 失敗、2 找不到 `tbs_browser.exe`、3 沒有系統管理員權限

## 聲明

非官方工具，與 Level Infinite、Proxima Beta、騰訊無關，使用風險自負。以 [MIT 授權](LICENSE)釋出。
