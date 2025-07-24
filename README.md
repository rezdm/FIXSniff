# FIXSniff
Parse FIX messages and display them in human-readable form

I need a tool to display contents of FIX messages in human-readable form. About 20 years ago, while working in DeutscheBank, we had something Java-based for such purpose. 
Trying to find it now -- no luck. Easier to quickly develop something.

```
dotnet restore
dotnet run
copy-paste a FIX message, e.g.
```

``8=FIX.4.4|9=178|35=D|49=SENDER|56=TARGET|34=1|52=20230101-10:30:00|11=ORDER123|21=1|55=MSFT|54=1|38=100|40=2|44=150.50|59=0|10=123``

Click on "Parse", hopefully it will print out the contents with some comments:

<img width="902" height="1112" alt="image" src="https://github.com/user-attachments/assets/6fa2ebe5-cf79-4b63-9810-c143c7cb9b78" />
