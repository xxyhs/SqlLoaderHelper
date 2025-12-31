This is a Visual Studio extension that helps you manage sql files in C# projects. Its main features include sql autocomplete in code and support navigation between code and sql file;

## Prerequisites
Add a configuration file in solution directory named `.sqlloadercfg.json` , and add the following content to it:
```
{
  "SQLRoot": "SQLs", 
  "SqlLoaderMetaPrefix": "Xdev"
}
```
- `SQLRoot`: The root directory where all your sql files are stored, relative to the solution directory.
- `SqlLoaderMetaPrefix`:  optional, the prefix of SqlLoader full meta name, if setted, the extension will count times of sql file referenced by code

This VSIX extension will determine whether to enable it based on whether SQLRoot is set.

Additionally, we need to add the following configuration to the .csproj file corresponding to the project containing the sql folder to ensure that all directories and sql files under the sql folder are completely output to the compiled folder after the project is compiled.

```
<ItemGroup>
	<Content Include="SQLs\**\*.sql">
		<CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
	</Content>
  </ItemGroup>
```

## SqlLoader Code

you need to create a class named `SqlLoader` in your project, the class should like below:

```csharp
namespace Xdev

public class SqlLoader
{
    private static readonly Dictionary<string, string> _cache = new Dictionary<string, string>();

    public static string Load(string fileName)
    {
        if (_cache.TryGetValue(fileName, out var sql))
            return sql;

        var realpath = fileName.Replace('.', Path.DirectorySeparatorChar);
        realpath += ".sql";
        var path = Path.Combine(AppContext.BaseDirectory, "SQLs", realpath);// SQLs is the folder name which contains all your sql files, you can change it to your own folder name
        sql = File.ReadAllText(path);
        _cache[fileName] = sql;
        return sql;
    }

}

```

## Features

1.The extension will watch all sql files in your project when you set the `SQLRoot` in .sln file, and covert sql files to code name list for auto-complete 
for example you store all sql files in a folder named `SQLs`,
```
your_solution.sln
project/
|-- OtherFiles/
|-- SQLs/
| |-- ListAreas.sql
| |-- Users/
| | |-- CreateUser.sql
| | |-- GetUsers.sql
| |-- Orders/
| | |-- CreateOrder.sql
| | |-- UpdateOrder.sql
| | |-- AfterSale/
| | | |-- CreateAfterSale.sql
```
the extension will compose a list like below:
[
	"ListAreas",
	"Users.CreateUser"
	"Users.GetUsers",
	"Orders.CreateOrder",
	"Orders.UpdateOrder",
    "Orders.AfterSale.CreateAfterSale"
]

When you type `SqlLoader.Load("Orde` in your code, the extension will provide you with a list like "Orders.CreateOrder", "Orders.UpdateOrder".

2.The extension can help you goto the sql file when you press `F12` Or `Ctrl + Click` on the <sqlcode> in SqlLoader.Load("<sqlcode>")


3.The extension can count times of sql file referenced by code, when you set `SqlLoaderMetaPrefix`, and it also support redirect from the sql file to referenced location in code.
in example code the SqlLoader.Load's full meta name is `Xdev.SqlLoader.Load`, so the SqlLoaderMetaPrefix is `Xdev`

## Demo
![Image](https://github.com/user-attachments/assets/ed8b0ef5-6493-4d0d-9156-aa032818911b)

## Tips

I Just test the extension in Visual Studio 2022 Community Version, if you find any issue in other versions, please let me know.