# Third-party components

ACWorldEdit uses the following external components. Their upstream licenses apply to those components;
this file does not license ACWorldEdit itself. WoW client assets are never distributed.

| Component | Source | License |
| --- | --- | --- |
| SharpZipLib 1.4.2 | https://github.com/icsharpcode/SharpZipLib | MIT |
| MySqlConnector 2.6.2 | https://github.com/mysql-net/MySqlConnector | MIT |
| DBCD 2.3.1 (with DBCD.IO, DBDefsLib) | https://github.com/wowdev/DBCD | MIT |
| WoWDBDefs table definitions (`Data/definitions`) | https://github.com/wowdev/WoWDBDefs, revision in `Data/definitions/revision.txt` | CC BY-SA 4.0 data; upstream license in `Data/definitions/LICENSE.md`; `.dbd` files copied without modification |
| CascLib (native, not shipped) | https://github.com/ladislav-zezula/CascLib, see `Tools/Get-CascLib.ps1` | MIT |

The Retail client profile reads the installed client's CASC storage and DB2 tables read-only. Retail
format knowledge was cross-checked against the wowdev community documentation
(https://wowdev.wiki/); no source from other editors is incorporated.
