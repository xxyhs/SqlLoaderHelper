
the extension will search for all the sql files in your project and provide you with a list of file names to choose from when you type `SqlLoader.Load("` in your code.

## SqlLoader Code
you need to create a class named `SqlLoader` in your project, the class should like below:

```csharp
public class SqlLoader
{
    private static readonly Dictionary<string, string> _cache = new Dictionary<string, string>();

    public static string Load(string fileName)
    {
        if (_cache.TryGetValue(fileName, out var sql))
            return sql;

        var realpath = fileName.Replace('.', Path.DirectorySeparatorChar);
        realpath += ".sql";
        var path = Path.Combine(AppContext.BaseDirectory, "SQLs", realpath);
        sql = File.ReadAllText(path);
        _cache[fileName] = sql;
        return sql;
    }

}

```

## Features

1.the extension will watch all sql files in your project, the extension will find all sql file's same parent folder as base directory and watch all sql file's change.
for example you store all sql files in a folder named `SQLs`,

SQLs
  >ListAreas.sql
  >Users
	- CreateUser.sql
	- GetUsers.sql
  >Orders
	- CreateOrder.sql
	- UpdateOrder.sql

the extension will compose a list like below:
[
	"ListAreas",
	"Users.CreateUser"
	"Users.GetUsers",
	"Orders.CreateOrder",
	"Orders.UpdateOrder"
]

When you type `SqlLoader.Load("Orde` in your code, the extension will provide you with a list like "Orders.CreateOrder", "Orders.UpdateOrder".


2.the extension can help you goto the sql file when you press `F12` Or `Ctrl + Click` on the <sqlcode> in SqlLoader.Load("<sqlcode>")


## Tips

I Just test the extension in Visual Studio 2022 Community Version, if you find any issue in other versions, please let me know.