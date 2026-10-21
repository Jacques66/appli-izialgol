program succlast(output);
type color = (red, green, blue);
var c: color;
begin c := green; c := succ(c); writeln(ord(c):1); c := succ(c) end.
