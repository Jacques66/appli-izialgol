program pagedemo(output);
begin
   write('before');
   page;
   writeln('after');
   page(output);
   writeln('end')
end.
