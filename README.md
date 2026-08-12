# ChuniLauncher
basically a better version of [SliderLaunch](https://github.com/dumcatt/SliderLaunch)

requires [.NET 8.0](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)

<img height="300" alt="image" src="https://github.com/user-attachments/assets/67e5cc32-6d44-4121-88a4-8f2ac546a941" />


# Configuration
games.ini
```
[Settings]
autoLaunch=1
autoLaunchTime=30
background= (path to your background image)
sliderPort=COM1
sliderSensitivity=20
led1Port=COM20 (ports of your 837-15093 LED boards)
led2Port=COM21
SideBrightness=30

[Game1]
game=game1.bat (path to your game)
logo=logo.png (path to your game logo)
colour=#ffffff
category=Category1
```
Font can be customized by adding font.ttf next to the exe
