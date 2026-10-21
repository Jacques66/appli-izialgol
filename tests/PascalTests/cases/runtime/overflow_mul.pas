program overflowmul(output);
var i: integer;
begin
   i := 16777216;            { 2^24 }
   writeln(i * i div 2:1);   { 2^47 }
   i := i * i * 2            { 2^49 }
end.
