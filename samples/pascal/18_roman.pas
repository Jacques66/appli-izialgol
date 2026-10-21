{ Roman numerals (after the User Manual, Jensen and Wirth): writes the
  powers of 2 in Roman notation. }
program roman(output);
var x, y: integer;
begin
   y := 1;
   repeat
      x := y;
      write(x:5, ' ');
      while x >= 1000 do begin write('M'); x := x - 1000 end;
      if x >= 500 then begin write('D'); x := x - 500 end;
      while x >= 100 do begin write('C'); x := x - 100 end;
      if x >= 50 then begin write('L'); x := x - 50 end;
      while x >= 10 do begin write('X'); x := x - 10 end;
      if x >= 5 then begin write('V'); x := x - 5 end;
      while x >= 1 do begin write('I'); x := x - 1 end;
      writeln;
      y := 2 * y
   until y > 5000
end.
