﻿using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

namespace eshopAPI.Models
{
    public class Cart
    {
        [Key]
        public long ID { get; set; }
        public User User { get; set; }
        public List<CartItem> Items { get; set; }
    }
}