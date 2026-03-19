# mesrunes-folly
A small Vintage Story mod changing rooms and cellars size

Mesrune wanted bigger rooms and custom cellars..!

Size is limited to 32, until further notice ^^'
Care about multi-chunks rooms : room system ain't designed to allow very big rooms and this mod will break /debug rooms hi/unhi (which I might fix, someday!)
Also, rooms are registered by chunk : in a multi-chunk room, containers might required you to interact with them to update their state...

 

About the config file (mesrunesfolly.json) :
name 	defaults to 	value limitations (defaults to relevant if out of range) 	description
MaxRoomSize 	14 	[2; 32] 	maximum room size
OnlyVolumeForCellar 	false 	  	if true, room system only cares about cellars' volume, thus cellars' size is limited by MaxRoomSize
MaxCellarSize 	7 	[2; MaxRoomSize] 	maximum cellar size
AlternateMaxCellarSize 	9 	[MaxCellarSize; MaxRoomSize] 	sets the maximum length one cellar's dimension can reach : has no effect if lower than or equal to MaxCellarSize 
MaxCellarVolume 	150 	  	if "OnlyVolumeForCellar = true", this defines the allowed cellars' volume
if "OnlyVolumeForCellar = false" and "AlternateMaxCellarSize > MaxCellarSize", this defines the maximum air volume allowed for a room to be considered a cellar (by default, you can dig 150 blocks in a 7x7x9 cuboid)

 

Default values yield vanilla behavior...

 


Special thanks to :
- Mesrune for the idea and thumbnail :-3
- Meikah for the awesomely artistic thumbnail reshade!